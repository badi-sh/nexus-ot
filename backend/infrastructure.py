import yaml
import subprocess
import os
import re

# --- CONFIGURATION ---
NETWORK_SUBNET = "192.168.1.0/24"
NETWORK_GATEWAY = "192.168.1.1"
COMPOSE_FILE = "docker-compose.yml"

def clean_docker_name(name):
    cleaned = re.sub(r'[^a-zA-Z0-9_.-]', '_', name)
    return cleaned.strip('_-').lower()

def get_network_context(asset_id, links, assets_map):
    allowed_inbound = []
    explicit_drop_inbound = []
    explicit_drop_outbound = []
    ids_mirror_ips = []
    neighbor_types = []
    gateway_ip = None

    for link in links:
        link_from = getattr(link, 'From', getattr(link, 'from_node', getattr(link, 'from', None)))
        link_to = getattr(link, 'To', getattr(link, 'to_node', getattr(link, 'to', None)))
        link_type = getattr(link, 'LinkType', 'Bidirectional')

        if link_from == asset_id:
            neighbor = assets_map.get(link_to)
            if not neighbor: continue
            
            n_ip = neighbor.Meta.get("ip")
            if not n_ip: continue

            neighbor_types.append(neighbor.Type)
            if neighbor.Type in ["Firewall", "IDS", "Router", "Switch"]:
                gateway_ip = n_ip
            if neighbor.Type == "IDS":
                ids_mirror_ips.append(n_ip)

            if link_type == "Unidirectional":
                explicit_drop_inbound.append(n_ip)
            else:
                allowed_inbound.append(n_ip)

        elif link_to == asset_id:
            neighbor = assets_map.get(link_from)
            if not neighbor: continue
            
            n_ip = neighbor.Meta.get("ip")
            if not n_ip: continue

            neighbor_types.append(neighbor.Type)
            if neighbor.Type in ["Firewall", "IDS", "Router", "Switch"]:
                gateway_ip = n_ip
            if neighbor.Type == "IDS":
                ids_mirror_ips.append(n_ip)

            allowed_inbound.append(n_ip)

            if link_type == "Unidirectional":
                explicit_drop_outbound.append(n_ip)

    return allowed_inbound, explicit_drop_inbound, explicit_drop_outbound, neighbor_types, gateway_ip, ids_mirror_ips

def generate_firewall_command(base_image, my_ip, allowed_inbound, explicit_drop_inbound, explicit_drop_outbound, node_type="Standard", gateway_ip=None, neighbor_types=None, ids_mirror_ips=None):
    if neighbor_types is None: neighbor_types = []
    if ids_mirror_ips is None: ids_mirror_ips = []

    setup_cmd = "echo 'Configuring Network...' && "
    
    # FIX: Install iptables for Alpine OR Debian (OpenPLC)
    if "alpine" in base_image and "nexus" not in base_image and "openplc" not in base_image and "postgres" not in base_image:
        setup_cmd += "apk add --no-cache iptables iproute2 && "
    elif "openplc" in base_image:
        setup_cmd += "apt-get update > /dev/null 2>&1 && apt-get install -y iptables iproute2 > /dev/null 2>&1 && "
    
    rules = [
        "iptables -F",              
        "iptables -A INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT", 
        "iptables -A INPUT -s 127.0.0.1 -j ACCEPT",
        f"iptables -A INPUT -s {NETWORK_GATEWAY} -j ACCEPT" 
    ]

    # THE MIRROR: Clones traffic to the IDS
    for ids_ip in ids_mirror_ips:
        rules.append(f"iptables -t mangle -A PREROUTING -j TEE --gateway {ids_ip}")
        rules.append(f"iptables -t mangle -A POSTROUTING -j TEE --gateway {ids_ip}")

    for ip in explicit_drop_inbound:
        rules.append(f"iptables -I INPUT 1 -s {ip} -j DROP")
    for ip in explicit_drop_outbound:
        rules.append(f"iptables -I OUTPUT 1 -d {ip} -j DROP")

    network_devices = ["Switch", "Hub", "Router", "Firewall", "IDS"]
    is_networked = any(t in network_devices for t in neighbor_types)

    if is_networked:
        rules.append("iptables -P INPUT ACCEPT") 
    else:
        rules.append("iptables -P INPUT DROP") 
        for ip in allowed_inbound:
            if ip: rules.append(f"iptables -A INPUT -s {ip} -j ACCEPT")

    if node_type == "Firewall":
         rules = ["iptables -F", "iptables -P INPUT ACCEPT"] 
         rules.append("iptables -A INPUT -s 192.168.1.0/24 -j LOG --log-prefix '[NEXUS_FW_BLOCK]: ' --log-level 4")
         rules.append(f"iptables -A INPUT -s {NETWORK_SUBNET} -j DROP")

    if node_type in ["Switch", "Hub", "IDS"]:
        setup_cmd += "sysctl -w net.ipv4.ip_forward=1 && "
        rules = ["iptables -F", "iptables -P FORWARD ACCEPT", "iptables -P INPUT ACCEPT"]

    if gateway_ip:
        setup_cmd += f"ip route add {gateway_ip}/32 dev eth0 && "
        setup_cmd += f"ip route add 192.168.1.0/25 via {gateway_ip} || true && "
        setup_cmd += f"ip route add 192.168.1.128/25 via {gateway_ip} || true && "

    firewall_script = " && ".join(rules)
    verify_cmd = "echo '--- ACTIVE FIREWALL RULES ---' && iptables -L -n -v"
    full_command = f"{setup_cmd} {firewall_script} && {verify_cmd} && tail -f /dev/null"
    
    return f'/bin/sh -c "{full_command}"'

def generate_compose(topology):
    services = {}
    assets_map = {asset.Id: asset for asset in topology.Assets}
    has_ids = False

    for asset in topology.Assets:
        if asset.Type == "Physical": continue 

        my_ip = asset.Meta.get("ip", "")
        allowed_inbound, explicit_drop_inbound, explicit_drop_outbound, neighbor_types, gateway_ip, ids_mirror_ips = get_network_context(asset.Id, topology.Links, assets_map)

        custom_name = asset.Meta.get("name", "").strip()
        base_name = custom_name if custom_name else f"{asset.Type}_{asset.Id}"
        container_name = clean_docker_name(base_name)

        role = asset.Meta.get("Role", "Standard")
        node_type = asset.Type 
        pull_policy = "if_not_present" 
        env_vars = {}
        volumes = []
        ports = []
        mem_limit = "256m"
        container_user = None

        if node_type == "IDS":
            has_ids = True
            image, pull_policy, mem_limit = "nexus_ids", "never", "2g"
            volumes = ["./logs/zeek:/opt/zeek/logs:rw"]
        elif node_type in ["Hybrid", "RTU", "MTU"]:
            image, pull_policy, mem_limit = "openplc:v3", "never", "1g"
            volumes.append("./logic:/work/logic")
            ports = ["502:502", "8080:8080"]
        elif node_type == "Firewall":
            image, pull_policy, mem_limit = "nexus_blue", "never", "512m"
        elif node_type == "Historian":
            image, mem_limit = "postgres:alpine", "512m"
            env_vars = {"POSTGRES_PASSWORD": "root", "POSTGRES_DB": "nexus_historian"}
            volumes = ["./images/historian_init.sql:/docker-entrypoint-initdb.d/init.sql"]
        elif node_type == "HMI":
            image, pull_policy, mem_limit = "nodered/node-red:latest", "always", "1g"
            ports = ["1880:1880"]
            volumes = ["./nodered_data:/data"]
            container_user = "root"
            for ip in allowed_inbound:
                 env_vars["PLC_IP"] = ip; break
        elif node_type == "Digital":
            if role == "RedTeam":
                image = "nexus_red"
                pull_policy, mem_limit = "never", "2g"
                ports = ["8888:8888", "8443:8443", "7010:7010"]
            elif role == "Adversary":
                image = "kalilinux/kali-rolling"
                pull_policy = "always"
                mem_limit = "2g"
                
                # Auto-deployment command for the Sandcat Agent
                agent_cmd = (
                    "apt-get update && apt-get install -y curl mbpoll && "
                    "curl -s -X POST -H \\\"file:sandcat.go\\\" -H \\\"platform:linux\\\" http://192.168.1.1:8888/file/download > sandcat && "
                    "chmod +x sandcat && "
                    "./sandcat -server http://192.168.1.1:8888 -group red -v & "
                    "tail -f /dev/null"
                )

                service_config = {
                    "image": image,
                    "container_name": container_name,
                    "hostname": container_name,
                    "pull_policy": pull_policy,
                    "networks": {"nexus_net": {}},
                    "cap_add": ["NET_ADMIN", "NET_RAW"],
                    "privileged": True,
                    "mem_limit": mem_limit,
                    "command": f'/bin/bash -c "{agent_cmd}"',
                    "tty": True,
                    "stdin_open": True
                }
                
                if my_ip:
                    service_config["networks"]["nexus_net"]["ipv4_address"] = my_ip

                services[container_name] = service_config
                continue
            else:
                image = "nexus_blue"
                pull_policy, mem_limit = "never", "2g"
        else:
            image = "alpine"

        service_config = {
            "image": image,
            "container_name": container_name,
            "hostname": container_name,
            "pull_policy": pull_policy,
            "networks": {"nexus_net": {}},
            "cap_add": ["NET_ADMIN", "NET_RAW"],
            "privileged": True,
            "mem_limit": mem_limit
        }

        if container_user: service_config["user"] = container_user
        if env_vars: service_config["environment"] = env_vars
        if volumes: service_config["volumes"] = volumes
        if ports: service_config["ports"] = ports

        if "postgres" not in image and "caldera" not in image and "nodered" not in image:
            if node_type == "IDS":
                service_config["command"] = (
                    '/bin/sh -c "mkdir -p /opt/zeek/logs && touch /opt/zeek/logs/conn.log && '
                    'ip link set eth0 promisc on && '
                    '/opt/zeek/bin/zeek -C -i eth0 local LogAscii::use_json=T & '
                    'while true; do tail -f --retry /opt/zeek/logs/conn.log || sleep 2; done"'
                )
            else:
                cmd = generate_firewall_command(image, my_ip, allowed_inbound, explicit_drop_inbound, explicit_drop_outbound, node_type, gateway_ip, neighbor_types, ids_mirror_ips)
                
                if "openplc" in image:
                    service_config["entrypoint"] = ""
                    clean_fw = cmd.replace('tail -f /dev/null', '').replace('"', '').replace('/bin/sh -c ', '').strip()
                    if clean_fw.endswith('&&'):
                        clean_fw = clean_fw[:-2].strip()
                        
                    # FIX: Safely wrap the physics script so a missing file doesn't crash the container
                    service_config["command"] = f'/bin/sh -c "pip install pymodbus shim > /dev/null 2>&1 && {clean_fw} && /work/start_openplc.sh & sleep 5 && (python3 /work/logic/rtu_tank.py || echo \\"WARNING: rtu_tank.py missing!\\") && tail -f /dev/null"'
                else:
                    service_config["command"] = cmd

        if my_ip:
            service_config["networks"]["nexus_net"]["ipv4_address"] = my_ip

        services[container_name] = service_config

    if has_ids:
        services["elasticsearch"] = {
            "image": "docker.elastic.co/elasticsearch/elasticsearch:7.12.1",
            "container_name": "nexus_elasticsearch",
            "environment": ["discovery.type=single-node", "ES_JAVA_OPTS=-Xms2g -Xmx2g"],
            "mem_limit": "4g",
            "ports": ["9200:9200"],  # <-- THIS IS THE MISSING LINK
            "networks": {"nexus_net": {"ipv4_address": "192.168.1.200"}}
        }

        services["kibana"] = {
            "image": "docker.elastic.co/kibana/kibana:7.12.1",
            "container_name": "nexus_kibana",
            "environment": ["ELASTICSEARCH_HOSTS=http://nexus_elasticsearch:9200"],
            "ports": ["5601:5601"],
            "mem_limit": "1g",
            "networks": {"nexus_net": {}},
            "depends_on": ["elasticsearch"]
        }

        services["filebeat"] = {
            "image": "docker.elastic.co/beats/filebeat:7.12.1",
            "container_name": "nexus_filebeat",
            "user": "root",
            "mem_limit": "512m",
            "volumes": ["./logs/zeek:/usr/share/filebeat/logs:ro", "./filebeat.yml:/usr/share/filebeat/filebeat.yml:ro"],
            "networks": {"nexus_net": {}},
            "depends_on": ["elasticsearch"]
        }   

    compose_data = {
        "services": services,
        "networks": {
            "nexus_net": {
                "driver": "bridge",
                "ipam": {
                    "config": [{"subnet": NETWORK_SUBNET, "gateway": NETWORK_GATEWAY}]
                }
            }
        }
    }
    return compose_data

def write_compose_file(data):
    with open(COMPOSE_FILE, 'w') as f:
        yaml.dump(data, f, default_flow_style=False)
    print(f"✅ Generated {COMPOSE_FILE}")

def deploy_infrastructure():
    print("🚀 Launching Cyber Range (Physics Edition)...")
    try:
        subprocess.run(["docker", "compose", "up", "-d", "--remove-orphans"], check=True)
        print("✅ Deployment Complete.")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Deployment Failed: {e}")
        return False
