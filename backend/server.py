from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any, Optional
import uvicorn
import networkx as nx
from scipy.optimize import fsolve

# --- IMPORT INFRASTRUCTURE LOGIC ---
# This pulls the logic we wrote in infrastructure.py
from infrastructure import generate_compose, write_compose_file, deploy_infrastructure

app = FastAPI()

# --- DATA MODELS ---
class AssetData(BaseModel):
    Id: str
    Type: str
    PosX: float = 0.0  # Added default to prevent validation errors if Godot omits it
    PosY: float = 0.0
    Meta: Dict[str, Any]

class LinkData(BaseModel):
    From: str
    FromPort: int
    To: str
    ToPort: int

class Topology(BaseModel):
    Assets: List[AssetData]
    Links: List[LinkData]

# --- PHYSICS ENGINE (Advanced) ---
def physics_kernel(unknown_pressures, G, free_nodes):
    """
    Solves for pressure equilibrium in the pipe network.
    """
    # 1. Update Graph with current guesses
    for i, node in enumerate(free_nodes):
        G.nodes[node]['pressure'] = unknown_pressures[i]
    
    equations = []
    
    # 2. Kirchhoff's Law for Fluids: Net flow at every node must be 0
    for node in free_nodes:
        net_flow = 0
        current_P = G.nodes[node]['pressure']
        
        for neighbor in G.neighbors(node):
            neighbor_P = G.nodes[neighbor]['pressure']
            edge_resistance = G.edges[node, neighbor].get('resistance', 1.0)
            
            # Flow = Delta_P / Resistance
            flow = (neighbor_P - current_P) / edge_resistance
            net_flow += flow
            
        equations.append(net_flow)
        
    return equations

# --- ENDPOINT 1: SIMULATION (Physics) ---
@app.post("/simulate")
async def run_simulation(topology: Topology):
    G = nx.Graph()
    
    # 1. Build the Graph Nodes
    # We only care about Physical nodes (Tanks) and Hybrid nodes (PLCs attached to physics)
    for asset in topology.Assets:
        if asset.Type in ['Physical', 'Hybrid']:
            cap_str = asset.Meta.get('capacity', '0')
            cap = float(cap_str) if cap_str else 0.0
            
            # Fixed = It is a Source (like a full tank or pump)
            # Not Fixed = It is a pipe/junction that needs solving
            is_fixed = cap > 0 
            
            G.add_node(asset.Id, pressure=cap, fixed=is_fixed)

    # 2. Build the Graph Edges
    for link in topology.Links:
        if G.has_node(link.From) and G.has_node(link.To):
            # Resistance could be dynamic based on pipe length later
            G.add_edge(link.From, link.To, resistance=1.0)

    # 3. Solve System of Equations
    physical_nodes = list(G.nodes)
    free_nodes = [n for n in physical_nodes if not G.nodes[n]['fixed']]
    
    if free_nodes:
        initial_guess = [0.0] * len(free_nodes)
        
        try:
            # fsolve finds the roots (where net flow = 0)
            solution = fsolve(physics_kernel, initial_guess, args=(G, free_nodes))
            
            for i, node in enumerate(free_nodes):
                G.nodes[node]['pressure'] = float(solution[i])
        except Exception as e:
            print(f"Physics Solver Error: {e}")

    # 4. Format Results for Godot
    results = {}
    for node in physical_nodes:
        results[node] = {
            "pressure": round(G.nodes[node]['pressure'], 2)
        }
    
    print(f"Physics Calculated for {len(results)} nodes.")
    return {"status": "success", "results": results}

# --- ENDPOINT 2: DEPLOYMENT (Docker) ---
@app.post("/deploy")
async def trigger_deployment(topology: Topology):
    print(f"Received deployment request for {len(topology.Assets)} assets.")
    
    try:
        # 1. Translate Godot Topology -> Docker Compose Config
        compose_data = generate_compose(topology)
        
        # 2. Save docker-compose.yml to disk
        write_compose_file(compose_data)
        
        # 3. Run 'docker compose up' and build images
        success = deploy_infrastructure()
        
        if success:
            return {"status": "success", "message": "Cyber Range Deployed Successfully"}
        else:
            # This returns a 200 OK with error status so Godot can parse the message easily
            return {"status": "error", "message": "Docker failed to start. Check server logs."}
            
    except Exception as e:
        print(f"Server Error during deployment: {e}")
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
