# Nexus-OT: Industrial Cybersecurity Cyber Range

Nexus-OT is a next-generation Industrial Cybersecurity Cyber Range that combines a visual network topology designer with a powerful infrastructure engine. It allows users to visually design Operational Technology (OT) environments, simulate physical processes, and deploy real-world containerized infrastructure for security testing, adversary emulation, and defense validation.

Built with **Godot 4.5 (C#)** for the frontend and a **Python/FastAPI** backend, Nexus-OT provides a seamless transition from conceptual architecture to a fully functional, live-monitored cyber range.

## 🚀 Features

- **Visual Topology Designer**: Drag-and-drop interface for creating complex industrial networks.
- **Purdue Model Compliance**: Automatic classification of assets into Purdue levels (Level 0 to Level 5).
- **Automated Deployment**: One-click generation of `docker-compose.yml` and live orchestration of containers.
- **Live Physics Engine**: Real-time simulation of fluid dynamics and pressure equilibrium in pipe networks.
- **Integrated SOC & Monitoring**: Built-in packet sniffing with Scapy and log aggregation via the ELK Stack (Elasticsearch, Kibana, Filebeat).
- **Adversary Emulation**: Integration with MITRE CALDERA for automated attack scenarios.
- **Digital Twin Visualization**: Real-time feedback of breaker states and process values within the graphical editor.

## 📁 Project Structure

```text
nexus-ot/
├── project.godot                  # Godot 4.5 project configuration
├── Nexus_OT.csproj               # .NET 8 C# project
├── GraphManager.cs               # Core logic for graph management and API calls
├── AssetNode.cs                  # Graphical representation of network assets
├── InspectorPanel.cs             # UI for modifying asset properties
├── LinkDataStore.cs              # Metadata storage for network connections
├── main.tscn                     # Main Godot scene
├── backend/                      # Backend services and infrastructure engine
│   ├── server.py                 # FastAPI server (API endpoint)
│   ├── infrastructure.py         # Docker Compose generation logic
│   ├── solver.py                 # Physics simulation engine
│   ├── soc.py                    # SOC monitoring and packet sniffing
│   ├── images/                   # Custom Dockerfiles (PLC, IDS, Red/Blue Teams)
│   ├── logic/                    # PLC logic and polling scripts
│   └── st_files/                 # IEC 61131-3 Structured Text programs
└── 3D_Assets/                    # High-quality 3D models for equipment
```

## 🛠️ Implementation & Deployment

### Prerequisites

- **Godot 4.5** (with .NET support)
- **Docker** & **Docker Compose**
- **Python 3.10+**
- **Gnome Terminal** (for auto-terminal execution)

### Steps to Run

1. **Start the Backend API**:
   ```bash
   cd backend
   python3 -m venv venv
   source venv/bin/activate
   pip install -r requirements.txt
   python3 server.py
   ```

2. **Launch the Godot Editor**:
   - Open Godot 4.5.
   - Import the `project.godot` file.
   - Press **F5** to run the project.

3. **Design and Build**:
   - Drag nodes from the sidebar.
   - Connect ports to define network links.
   - Configure IPs and roles in the Inspector Panel.
   - Click **"Build Network"** to deploy the live infrastructure.

4. **Monitor and Test**:
   - Use the **"Open Terminal"** button to access live containers.
   - Access **Kibana** at `http://localhost:5601` for log analysis.
   - Access **Node-RED** at `http://localhost:1880` for HMI control.

## 🛡️ Use Cases

- **Security Research**: Analyze the impact of ICS-specific attacks on industrial protocols like Modbus TCP.
- **Training & Education**: Teach students about the Purdue Model, network segmentation, and OT defense-in-depth.
- **Incident Response Testing**: Validate SOC alerts and incident response playbooks against real containerized adversaries.
- **Protocol Validation**: Test new industrial protocols or security appliances in a safe, reproducible environment.

## ⚠️ Known Limitations

*   **Network Segmentation**: Currently relies on container-level `iptables` rather than true VLAN isolation.
*   **Resource Intensity**: Running the full ELK stack and multiple PLCs requires significant RAM (16GB+ recommended).
*   **Static IPs**: Some attack payloads and SOC monitoring scripts currently use hardcoded IP references.

## 📜 License

This project is released under the MIT License.
