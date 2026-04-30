using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json; 
using System.Text;      

public partial class GraphManager : GraphEdit
{
	[Export] public PackedScene AssetScene;
	
	// UI & System References
	private FileDialog _fileDialog; 
	private InspectorPanel _inspector;
	private HttpRequest _simRequest; 

	private bool _isSaving = false; 

	// --- Data Structures ---
	public class Topology
	{
		public List<AssetData> Assets { get; set; } = new List<AssetData>();
		public List<LinkData> Links { get; set; } = new List<LinkData>();
	}

	public class AssetData
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public float PosX { get; set; } 
		public float PosY { get; set; } 
		public Dictionary<string, string> Meta { get; set; }
	}

	public class LinkData
	{
		public string From { get; set; }
		public int FromPort { get; set; }
		public string To { get; set; }
		public int ToPort { get; set; }
		public string LinkType { get; set; } // Stores Unidirectional/Bidirectional
	}

	public override void _Ready()
	{
		// FIX: Disabled automatic severing so you can stack multiple connections on one port!
		RightDisconnects = false;
		
		// 1. Graph Event Signals
		ConnectionRequest += OnConnectionRequest;
		DisconnectionRequest += OnDisconnectionRequest;
		Connect("delete_nodes_request", new Callable(this, MethodName.OnDeleteNodesRequest));

		NodeSelected += OnNodeSelected;
		NodeDeselected += OnNodeDeselected;

		// 2. Setup File Dialog 
		_fileDialog = GetParent().GetNodeOrNull<FileDialog>("FileDialog");
		if (_fileDialog != null)
		{
			_fileDialog.FileSelected += OnFileSelected;
		}

		_inspector = GetNodeOrNull<InspectorPanel>("../CanvasLayer/InspectorPanel");
		
		_simRequest = GetNodeOrNull<HttpRequest>("SimRequest");
		if (_simRequest != null)
		{
			_simRequest.RequestCompleted += OnSimulationCompleted;
		}
	}

	// ==========================================
	//            GRAPH INTERACTION
	// ==========================================

	private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
	{
		Node fromNodeObj = GetNodeOrNull(fromNode.ToString());
		Node toNodeObj = GetNodeOrNull(toNode.ToString());
		
		if (fromNodeObj is GraphNode fn && toNodeObj is GraphNode tn)
		{
			if (fn.PositionOffset.X > tn.PositionOffset.X)
			{
				var tempNode = fromNode;
				var tempPort = fromPort;
				fromNode = toNode;
				fromPort = toPort;
				toNode = tempNode;
				toPort = tempPort;
			}
		}

		ConnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
		
		LinkDataStore.SetLinkType(fromNode.ToString(), toNode.ToString(), "Bidirectional");
		RefreshInspectorIfSelected(fromNode, toNode);
	}

	private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
	{
		DisconnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
		
		LinkDataStore.RemoveLink(fromNode.ToString(), toNode.ToString());
		GD.Print($"[Network] Severed connection between {fromNode} and {toNode}");
		
		RefreshInspectorIfSelected(fromNode, toNode);
	}

	private void RefreshInspectorIfSelected(StringName fromNode, StringName toNode)
	{
		if (_inspector == null) return;

		foreach (Node child in GetChildren())
		{
			if (child is AssetNode gn && gn.Selected)
			{
				if (gn.Name == fromNode || gn.Name == toNode)
				{
					_inspector.LoadNode(gn);
				}
			}
		}
	}

	// UPGRADED: Made public so AssetNode context menu can trigger it
	public void OnDeleteNodesRequest(Godot.Collections.Array nodes)
	{
		foreach (var nodeItem in nodes)
		{
			StringName nodeName = nodeItem.As<StringName>();
			DisconnectAllFromNode(nodeName.ToString());
			
			Node node = GetNodeOrNull(nodeName.ToString());
			if (node != null) node.QueueFree();
		}
		if (_inspector != null) _inspector.ClearSelection();
	}

	public void OnDeleteButtonPressed()
	{
		var selectedNodes = new Godot.Collections.Array();
		foreach (Node child in GetChildren())
		{
			if (child is GraphNode gn && gn.Selected)
				selectedNodes.Add(gn.Name);
		}
		if (selectedNodes.Count > 0) OnDeleteNodesRequest(selectedNodes);
	}

	// UPGRADED: Made public so AssetNode context menu can trigger it
	public void DisconnectAllFromNode(string nodeName)
	{
		var connectionList = GetConnectionList();
		foreach (var conn in connectionList)
		{
			string from = conn["from_node"].AsString();
			string to = conn["to_node"].AsString();
			
			if (from == nodeName || to == nodeName)
			{
				OnDisconnectionRequest(from, conn["from_port"].AsInt32(), to, conn["to_port"].AsInt32());
			}
		}
	}

	// NEW: Method for AssetNode context menu to snipe specific wires
	public void DisconnectSpecificNodes(string nodeA, string nodeB)
	{
		var connections = GetConnectionList();
		foreach (var conn in connections)
		{
			string from = conn["from_node"].AsString();
			string to = conn["to_node"].AsString();
			
			if ((from == nodeA && to == nodeB) || (from == nodeB && to == nodeA))
			{
				OnDisconnectionRequest(from, conn["from_port"].AsInt32(), to, conn["to_port"].AsInt32());
			}
		}
	}

	private void OnNodeSelected(Node node)
	{
		if (node is AssetNode asset && _inspector != null) _inspector.LoadNode(asset);
	}

	private void OnNodeDeselected(Node node)
	{
		if (_inspector != null) _inspector.ClearSelection();
	}

	public void AddNewAssetNode()
	{
		if (AssetScene == null) return;
		AssetNode newNode = AssetScene.Instantiate<AssetNode>();
		newNode.Name = "Node_" + Time.GetTicksMsec(); 
		AddChild(newNode);
		newNode.PositionOffset = (ScrollOffset + new Vector2(100, 100));
	}

	// ==========================================
	//            DATA EXPORT & SAVE/LOAD
	// ==========================================

	private Topology BuildTopologyFromGraph()
	{
		Topology netlist = new Topology();

		foreach (Node child in GetChildren())
		{
			if (child is AssetNode asset)
			{
				netlist.Assets.Add(new AssetData 
				{
					Id = asset.Name, 
					Type = asset.Type.ToString(), 
					PosX = asset.PositionOffset.X, 
					PosY = asset.PositionOffset.Y, 
					Meta = asset.Metadata
				});
			}
		}

		var godotConnections = GetConnectionList();
		foreach (var conn in godotConnections)
		{
			string from = conn["from_node"].AsString();
			string to = conn["to_node"].AsString();
			string linkType = LinkDataStore.GetLinkType(from, to);

			netlist.Links.Add(new LinkData 
			{
				From = from,
				FromPort = conn["from_port"].AsInt32(),
				To = to,
				ToPort = conn["to_port"].AsInt32(),
				LinkType = linkType 
			});
		}
		return netlist;
	}

	public void OnSaveButtonPressed()
	{
		if (_fileDialog == null) return;
		
		_isSaving = true;
		_fileDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
		_fileDialog.PopupCentered(new Vector2I(600, 400));
	}

	public void OnLoadButtonPressed()
	{
		if (_fileDialog == null) return;
		
		_isSaving = false;
		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
		_fileDialog.PopupCentered(new Vector2I(600, 400));
	}

	private void OnFileSelected(string path)
	{
		if (_isSaving)
		{
			Topology netlist = BuildTopologyFromGraph();
			var options = new JsonSerializerOptions { WriteIndented = true };
			string jsonString = JsonSerializer.Serialize(netlist, options);
			File.WriteAllText(path, jsonString);
			GD.Print($"✅ Saved to: {path}");
		}
		else
		{
			LoadTopologyFromFile(path);
		}
	}

	private void LoadTopologyFromFile(string path)
	{
		if (!File.Exists(path)) return;

		try 
		{
			string jsonString = File.ReadAllText(path);
			Topology netlist = JsonSerializer.Deserialize<Topology>(jsonString);

			ClearConnections(); 
			LinkDataStore.LinkTypes.Clear(); 

			foreach (Node child in GetChildren())
			{
				if (child is AssetNode) child.QueueFree();
			}

			if (AssetScene == null) { GD.PrintErr("AssetScene missing!"); return; }

			foreach (var assetData in netlist.Assets)
			{
				AssetNode newNode = AssetScene.Instantiate<AssetNode>();
				
				newNode.Name = assetData.Id; 
				AddChild(newNode); 

				if (Enum.TryParse(assetData.Type, out AssetNode.NodeType parsedType))
					newNode.Type = parsedType;
				
				newNode.Metadata = assetData.Meta;
				newNode.PositionOffset = new Vector2(assetData.PosX, assetData.PosY);
			}

			string linksJson = JsonSerializer.Serialize(netlist.Links);
			CallDeferred(nameof(RestoreConnections), linksJson);

			GD.Print("✅ Topology Loaded! (Wiring deferred...)");
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Load Failed: {e.Message}");
		}
	}

	private void RestoreConnections(string linksJson)
	{
		var links = JsonSerializer.Deserialize<List<LinkData>>(linksJson);
		int successCount = 0;

		foreach (var link in links)
		{
			Error err = ConnectNode(link.From, link.FromPort, link.To, link.ToPort);
			if (err == Error.Ok) 
			{
				successCount++;
				LinkDataStore.SetLinkType(link.From, link.To, link.LinkType ?? "Bidirectional");
			}
		}
		GD.Print($"⚡ Wires Restored: {successCount}/{links.Count}");
	}

	// ==========================================
	//        LIVE SIMULATION & DEPLOYMENT
	// ==========================================

	public void RunLiveSimulation()
	{
		if (_simRequest == null) return;

		Topology netlist = BuildTopologyFromGraph();
		string jsonString = JsonSerializer.Serialize(netlist);
		string[] headers = { "Content-Type: application/json" };
		_simRequest.Request("http://127.0.0.1:8000/simulate", headers, HttpClient.Method.Post, jsonString);
		GD.Print("Sending Status Request...");
	}

	public void RunDeployment()
	{
		if (_simRequest == null) return;

		Topology netlist = BuildTopologyFromGraph();
		string jsonString = JsonSerializer.Serialize(netlist);
		string[] headers = { "Content-Type: application/json" };
		_simRequest.Request("http://127.0.0.1:8000/deploy", headers, HttpClient.Method.Post, jsonString);
		GD.Print("Sending Deployment Request...");
	}

	private void OnSimulationCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		if (responseCode == 200)
		{
			string responseText = Encoding.UTF8.GetString(body);
			try
			{
				var jsonDoc = JsonDocument.Parse(responseText);
				var root = jsonDoc.RootElement;

				if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
				{
					if (root.TryGetProperty("results", out var results))
					{
						int updatesCount = 0;
						foreach (var property in results.EnumerateObject())
						{
							string nodeId = property.Name;
							
							// Look for 'state' instead of 'pressure' for our Digital Breakers
							if(property.Value.TryGetProperty("state", out var stateElement))
							{
								bool isClosed = stateElement.GetBoolean();
								Node n = GetNodeOrNull(nodeId);
								if (n is AssetNode asset && asset.Type == AssetNode.NodeType.Digital)
								{
									asset.UpdateBreakerState(isClosed);
									updatesCount++;
								}
							}
						}
						GD.Print($"Digital Twin Synced! Updated {updatesCount} breakers.");
					}
					else if (root.TryGetProperty("message", out var message))
					{
						string msgString = message.GetString();
						GD.Print("SERVER: " + msgString);
						if (msgString.ToLower().Contains("success") && _inspector != null)
							_inspector.SetNetworkReady(true);
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"JSON Parse Error: {ex.Message}");
			}
		}
		else
		{
			GD.PrintErr($"Request Failed: {responseCode}");
		}
	}
}
