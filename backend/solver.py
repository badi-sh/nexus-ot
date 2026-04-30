import json
import networkx as nx
import numpy as np
from scipy.optimize import fsolve

import os

# --- CONFIGURATION ---
NETLIST_PATH = os.path.join(os.path.dirname(__file__), "netlist.json") # Relative to this script
# ---------------------

def load_topology(path):
    with open(path, 'r') as f:
        data = json.load(f)
    return data

def build_physics_graph(data):
    """
    Converts the JSON Netlist into a NetworkX Graph.
    Only includes 'Physical' or 'Hybrid' nodes.
    """
    G = nx.Graph()
    
    # 1. Add Nodes
    for asset in data['Assets']:
        # We assume 'Digital' nodes (like PC) don't have water pressure
        if asset['Type'] in ['Physical', 'Hybrid']:
            # Use the "Capacity" value as a proxy for "Internal Pressure/Head"
            # For this test: Capacity > 50 means it's a PUMP (Source)
            pressure_source = float(asset['Meta'].get('capacity', 0))
            G.add_node(asset['Id'], pressure=pressure_source, fixed=pressure_source > 0)

    # 2. Add Edges (Pipes)
    for link in data['Links']:
        if G.has_node(link['From']) and G.has_node(link['To']):
            # Resistance represents pipe friction (1.0 is standard)
            G.add_edge(link['From'], link['To'], resistance=1.0)
            
    return G

def physics_kernel(unknown_pressures, G, free_nodes):
    """
    The Core Math Solver.
    Kirchhoff's Law: Sum of flows into a node must be zero.
    Flow = (P_neighbor - P_node) / Resistance
    """
    # 1. Update graph with current guesses
    for i, node in enumerate(free_nodes):
        G.nodes[node]['pressure'] = unknown_pressures[i]
        
    equations = []
    
    # 2. Build equations for every free node
    for node in free_nodes:
        net_flow = 0
        current_P = G.nodes[node]['pressure']
        
        for neighbor in G.neighbors(node):
            neighbor_P = G.nodes[neighbor]['pressure']
            resistance = G[node][neighbor]['resistance']
            
            # Flow from Neighbor -> Node
            flow = (neighbor_P - current_P) / resistance
            net_flow += flow
            
        equations.append(net_flow) # We want this to be 0
        
    return equations

def run_simulation():
    print("--- NEXUS OT PHYSICS ENGINE v1.0 ---")
    
    # 1. Load Data
    try:
        data = load_topology(NETLIST_PATH)
        print(f"Loaded {len(data['Assets'])} assets and {len(data['Links'])} links.")
    except FileNotFoundError:
        print("Error: netlist.json not found. Did you export from Godot?")
        return

    # 2. Build Graph
    G = build_physics_graph(data)
    physical_nodes = list(G.nodes)
    print(f"Physical Sub-System: {len(physical_nodes)} nodes active.")

    if len(physical_nodes) < 2:
        print("Not enough physical nodes to simulate flow. Connect at least 2 Physical/Hybrid nodes.")
        return

    # 3. Identify Fixed vs Free nodes
    # Fixed = Pumps/Sources (User set Capacity > 0)
    # Free = Pipes/Junctions (We need to calculate their pressure)
    fixed_nodes = [n for n in physical_nodes if G.nodes[n]['fixed']]
    free_nodes = [n for n in physical_nodes if not G.nodes[n]['fixed']]
    
    print(f"Sources (Pumps): {fixed_nodes}")
    print(f"Passive (Pipes): {free_nodes}")

    if not fixed_nodes:
        print("Warning: No Pressure Sources! System is static.")
        return

    # 4. Solve System of Equations
    if free_nodes:
        initial_guess = [0.0] * len(free_nodes)
        solution = fsolve(physics_kernel, initial_guess, args=(G, free_nodes))
        
        # Apply solution back to graph
        for i, node in enumerate(free_nodes):
            G.nodes[node]['pressure'] = solution[i]

    # 5. Output Results
    print("\n--- SIMULATION RESULTS ---")
    for node in physical_nodes:
        p = G.nodes[node]['pressure']
        role = "SOURCE" if G.nodes[node]['fixed'] else "PIPE"
        print(f"Node {node} [{role}]: Pressure = {p:.2f} Bar")

if __name__ == "__main__":
    run_simulation()
