using Godot;
using System;
using System.Collections.Generic;

public partial class AssetNode : GraphNode
{
	// --- ENUMS ---
	public enum NodeType 
	{ 
		Digital, Hybrid, Firewall, IDS, Historian, 
		RTU, MTU, HMI, Switch, Hub, Router, DataDiode, Adversary
	}

	public enum PurdueLayer
	{
		Undeclared,
		Level0_Physical, Level1_BasicControl, Level2_Supervisory, 
		Level3_Operations, Level3_5_DMZ, Level4_Enterprise, Level5_External
	}

	// --- CACHED UI STYLES ---
	private StyleBoxFlat _titleStyle;
	private StyleBoxFlat _bodyStyle;
	private PopupMenu _contextMenu;

	// --- PROPERTIES ---
	private NodeType _type = NodeType.Hybrid;
	[Export] 
	public NodeType Type 
	{
		get => _type;
		set 
		{
			_type = value;
			if (Metadata == null) Metadata = new Dictionary<string, string>();
			Metadata["Type"] = _type.ToString();
			
			AutoAssignPurdueLayer(); 
			ConfigurePorts(); 
			RefreshVisuals(); 
		}
	}

	private PurdueLayer _currentPurdueLayer = PurdueLayer.Undeclared;
	public PurdueLayer CurrentPurdueLayer 
	{ 
		get => _currentPurdueLayer;
		set 
		{
			_currentPurdueLayer = value;
			if (Metadata == null) Metadata = new Dictionary<string, string>();
			Metadata["PurdueLayer"] = _currentPurdueLayer.ToString(); 
		}
	}

	public Dictionary<string, string> Metadata = new Dictionary<string, string>();

	public string AssetName
	{
		get { return Title; }
		set { Title = value; }
	}

	public AssetNode()
	{
		Metadata = new Dictionary<string, string>();
	}

	public override void _Ready()
	{
		Title = "New Asset";
		Resizable = true;
		
		InitStyles();

		if (!Metadata.ContainsKey("id")) Metadata["id"] = Name;
		if (!Metadata.ContainsKey("ip")) Metadata["ip"] = $"192.168.1.{(GD.Randi() % 252) + 2}"; 
		
		Metadata["Type"] = Type.ToString(); 

		AutoAssignPurdueLayer();
		ConfigurePorts(); 
		RefreshVisuals();
	}

	// --- CISCO PACKET TRACER STYLE PORTS ---
	public void ConfigurePorts()
	{
		// 1. Clear existing dynamic UI elements
		foreach (Node child in GetChildren())
		{
			if (child is Label lbl && (lbl.Name.ToString().StartsWith("PortLabel") || lbl.Text == "Data Link"))
			{
				RemoveChild(child);
				child.QueueFree();
			}
			if (child is Button btn && btn.Name == "ManagePortsBtn")
			{
				RemoveChild(child);
				child.QueueFree();
			}
		}

		// 2. Add single unified Data Link for all nodes
		Label dataLink = new Label();
		dataLink.Name = "PortLabel_Main";
		dataLink.Text = "Data Link";
		dataLink.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(dataLink);

		// 3. Add 'Manage Ports' button for network/control equipment
		if (_type == NodeType.Switch || _type == NodeType.Router || _type == NodeType.RTU)
		{
			Button managePortsBtn = new Button();
			managePortsBtn.Name = "ManagePortsBtn";
			managePortsBtn.Text = "Open Port View";
			managePortsBtn.CustomMinimumSize = new Vector2(0, 30);
			managePortsBtn.Pressed += ShowPortManager;
			
			AddChild(managePortsBtn);
		}
	}

	private void ShowPortManager()
	{
		int maxPorts = (_type == NodeType.Switch || _type == NodeType.Router) ? 8 : 4;

		GraphEdit graph = GetParent() as GraphEdit;
		List<AssetNode> connectedNodes = new List<AssetNode>();

		if (graph != null)
		{
			Godot.Collections.Array<Godot.Collections.Dictionary> connections = graph.GetConnectionList();
			foreach (Godot.Collections.Dictionary conn in connections)
			{
				string fromNodeId = conn["from_node"].AsString();
				string toNodeId = conn["to_node"].AsString();

				if (fromNodeId == Name) connectedNodes.Add(graph.GetNode<AssetNode>(toNodeId));
				if (toNodeId == Name) connectedNodes.Add(graph.GetNode<AssetNode>(fromNodeId));
			}
		}

		AcceptDialog portDialog = new AcceptDialog();
		portDialog.Title = $"{Title} - Port Management";
		portDialog.MinSize = new Vector2I(350, 400);

		ScrollContainer scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(330, 320);
		
		VBoxContainer vbox = new VBoxContainer();
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(vbox);

		for (int i = 1; i <= maxPorts; i++)
		{
			HBoxContainer hbox = new HBoxContainer();
			Label portLabel = new Label() { Text = $"Port {i}:", CustomMinimumSize = new Vector2(60, 0) };
			OptionButton dropDown = new OptionButton();
			dropDown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			dropDown.AddItem("--- Empty ---", 0);
			dropDown.SetItemMetadata(0, "");

			int itemIndex = 1;
			int selectedIndex = 0;
			string savedNodeId = Metadata.ContainsKey($"Port_{i}") ? Metadata[$"Port_{i}"] : "";

			foreach (AssetNode node in connectedNodes)
			{
				dropDown.AddItem($"{node.Title} ({node.Name})", itemIndex);
				dropDown.SetItemMetadata(itemIndex, node.Name.ToString());

				if (node.Name.ToString() == savedNodeId)
				{
					selectedIndex = itemIndex;
				}
				itemIndex++;
			}

			dropDown.Select(selectedIndex);

			int currentPort = i; 
			dropDown.ItemSelected += (long index) => 
			{
				string selectedId = dropDown.GetItemMetadata((int)index).AsString();
				if (string.IsNullOrEmpty(selectedId))
				{
					Metadata.Remove($"Port_{currentPort}");
				}
				else
				{
					Metadata[$"Port_{currentPort}"] = selectedId;
					GD.Print($"Assigned {selectedId} to {_type} Port {currentPort}");
				}
			};

			hbox.AddChild(portLabel);
			hbox.AddChild(dropDown);
			vbox.AddChild(hbox);
		}

		portDialog.AddChild(scroll);
		AddChild(portDialog);
		
		portDialog.Confirmed += () => portDialog.QueueFree();
		portDialog.Canceled += () => portDialog.QueueFree();

		portDialog.PopupCentered();
	}

	// --- GUI INPUT (HMI & Right-Click Menu) ---
	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			// Left Double-Click: Open HMI
			if (mouseEvent.DoubleClick && mouseEvent.ButtonIndex == MouseButton.Left && _type == NodeType.HMI)
			{
				GD.Print("Launching HMI Web Interface...");
				OS.ShellOpen("http://localhost:1880");
			}
			// Right-Click: Open Context Menu
			else if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
			{
				ShowContextMenu(mouseEvent.GlobalPosition);
			}
		}
	}

	private void ShowContextMenu(Vector2 position)
	{
		if (_contextMenu != null) _contextMenu.QueueFree();

		_contextMenu = new PopupMenu();
		_contextMenu.IdPressed += OnContextMenuIdPressed;
		AddChild(_contextMenu);

		Node parentGraph = GetParent();
		if (!(parentGraph is GraphEdit)) return;

		var connections = parentGraph.Call("get_connection_list").AsGodotArray<Godot.Collections.Dictionary>();
		int itemId = 100; // Start at 100 to avoid ID collisions
		bool hasConnections = false;

		foreach (var conn in connections)
		{
			string from = conn["from_node"].AsString();
			string to = conn["to_node"].AsString();

			string targetId = "";
			if (from == Name) targetId = to;
			else if (to == Name) targetId = from;

			if (targetId != "")
			{
				Node targetObj = parentGraph.GetNodeOrNull(targetId);
				string title = (targetObj is AssetNode a) ? a.Title : targetId;
				
				_contextMenu.AddItem($"Sever Link to {title}", itemId);
				_contextMenu.SetItemMetadata(_contextMenu.GetItemCount() - 1, targetId);
				itemId++;
				hasConnections = true;
			}
		}

		if (hasConnections)
		{
			_contextMenu.AddSeparator();
			_contextMenu.AddItem("Sever All Connections", 0);
		}
		
		_contextMenu.AddItem("Delete Asset", 1);
		
		_contextMenu.Position = new Vector2I((int)position.X, (int)position.Y);
		_contextMenu.Popup();
	}

	private void OnContextMenuIdPressed(long id)
	{
		Node parentGraph = GetParent();
		if (parentGraph == null) return;

		if (id == 0) // Sever All
		{
			parentGraph.Call("DisconnectAllFromNode", Name);
			for(int i=1; i<=8; i++) Metadata.Remove($"Port_{i}"); // Clean Packet Tracer memory
		}
		else if (id == 1) // Delete Node
		{
			var nodes = new Godot.Collections.Array { Name };
			parentGraph.Call("OnDeleteNodesRequest", nodes);
		}
		else if (id >= 100) // Sever Specific Target
		{
			int itemIdx = _contextMenu.GetItemIndex((int)id);
			string targetNodeName = _contextMenu.GetItemMetadata(itemIdx).AsString();

			parentGraph.Call("DisconnectSpecificNodes", Name, targetNodeName);

			// Scrub the severed node from our Packet Tracer memory
			for(int i=1; i<=8; i++) 
			{
				if (Metadata.ContainsKey($"Port_{i}") && Metadata[$"Port_{i}"] == targetNodeName)
					Metadata.Remove($"Port_{i}");
			}
		}
	}

	// --- LOGIC METHODS ---
	public void AutoAssignPurdueLayer()
	{
		switch (Type)
		{
			case NodeType.Hybrid:
			case NodeType.RTU:
			case NodeType.MTU:      CurrentPurdueLayer = PurdueLayer.Level1_BasicControl; break;
			case NodeType.HMI:      CurrentPurdueLayer = PurdueLayer.Level2_Supervisory; break;
			case NodeType.Historian:CurrentPurdueLayer = PurdueLayer.Level3_Operations; break;
			case NodeType.DataDiode: 
			case NodeType.Firewall:
			case NodeType.IDS:      CurrentPurdueLayer = PurdueLayer.Level3_5_DMZ; break;
			case NodeType.Router:   CurrentPurdueLayer = PurdueLayer.Level4_Enterprise; break;
			case NodeType.Adversary:CurrentPurdueLayer = PurdueLayer.Level5_External; break; 
			default:                CurrentPurdueLayer = PurdueLayer.Undeclared; break; 
		}
	}

	public void UpdateVisuals()
	{
		RefreshVisuals();
	}

	public void UpdateData(string key, string value)
	{
		if (Metadata == null) Metadata = new Dictionary<string, string>();
		Metadata[key] = value;
		if (key == "Role") RefreshVisuals();
	}

	// --- VISUAL STYLING LOGIC ---
	private void InitStyles()
	{
		_titleStyle = new StyleBoxFlat();
		_titleStyle.CornerRadiusTopLeft = 5;
		_titleStyle.CornerRadiusTopRight = 5;
		_titleStyle.ContentMarginLeft = 10;
		_titleStyle.ContentMarginRight = 10;

		_bodyStyle = new StyleBoxFlat();
		_bodyStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f); 
		_bodyStyle.CornerRadiusBottomLeft = 5;
		_bodyStyle.CornerRadiusBottomRight = 5;

		AddThemeStyleboxOverride("titlebar", _titleStyle);
		AddThemeStyleboxOverride("titlebar_selected", _titleStyle);
		AddThemeStyleboxOverride("panel", _bodyStyle);
		AddThemeStyleboxOverride("panel_selected", _bodyStyle);
	}

	public void RefreshVisuals()
	{
		Color targetColor = new Color(0.3f, 0.3f, 0.3f); 

		if (Metadata.ContainsKey("name") && !string.IsNullOrEmpty(Metadata["name"]))
			Title = Metadata["name"];

		switch (_type)
		{
			case NodeType.Hybrid:   targetColor = new Color(0.2f, 0.7f, 0.2f); break;
			case NodeType.Firewall: targetColor = new Color(1.0f, 0.5f, 0.0f); break;
			case NodeType.IDS:      targetColor = new Color(0.6f, 0.2f, 0.8f); break;
			case NodeType.Historian:targetColor = new Color(0.0f, 0.6f, 0.8f); break;
			case NodeType.DataDiode:targetColor = new Color(0.0f, 0.8f, 0.6f); break; 
			case NodeType.RTU:      targetColor = new Color(0.1f, 0.5f, 0.2f); break;
			case NodeType.MTU:      targetColor = new Color(0.8f, 0.7f, 0.1f); break;
			case NodeType.Switch:
			case NodeType.Hub:
			case NodeType.Router:   targetColor = new Color(0.4f, 0.4f, 0.4f); break;
			case NodeType.Adversary:targetColor = new Color(0.6f, 0.0f, 0.0f); break; 
			case NodeType.Digital:
				string role = Metadata.ContainsKey("Role") ? Metadata["Role"] : "";
				if (role == "RedTeam")       targetColor = new Color(0.8f, 0.1f, 0.1f);
				else if (role == "BlueTeam") targetColor = new Color(0.1f, 0.3f, 0.9f);
				else                         targetColor = new Color(0.3f, 0.4f, 0.6f);
				break;
		}
		
		PaintTitleBar(targetColor);

		// Standardize all ports visually to a single slot
		int slotIndex = 0;
		foreach (Node child in GetChildren())
		{
			if (child is Control && child.Name.ToString().StartsWith("PortLabel")) 
			{
				if (_type == NodeType.DataDiode)
					SetSlot(slotIndex, true, 0, new Color(0.2f, 0.9f, 0.2f), true, 0, new Color(0.9f, 0.2f, 0.2f));
				else
					SetSlot(slotIndex, true, 0, new Color(1, 1, 1), true, 0, new Color(1, 1, 1));
				
				slotIndex++;
			}
		}
	}
	
	private void PaintTitleBar(Color color)
	{
		_titleStyle.BgColor = color;

		if (CurrentPurdueLayer == PurdueLayer.Undeclared)
		{
			Color warningColor = new Color(1.0f, 0.0f, 0.0f);
			
			_titleStyle.BorderWidthTop = 3;
			_titleStyle.BorderWidthLeft = 3;
			_titleStyle.BorderWidthRight = 3;
			_titleStyle.BorderColor = warningColor;

			_bodyStyle.BorderWidthBottom = 3;
			_bodyStyle.BorderWidthLeft = 3;
			_bodyStyle.BorderWidthRight = 3;
			_bodyStyle.BorderColor = warningColor;
		}
		else
		{
			_titleStyle.BorderWidthTop = 0;
			_titleStyle.BorderWidthLeft = 0;
			_titleStyle.BorderWidthRight = 0;

			_bodyStyle.BorderWidthBottom = 0;
			_bodyStyle.BorderWidthLeft = 0;
			_bodyStyle.BorderWidthRight = 0;
		}
	}

	// --- DIGITAL TWIN STATE INDICATOR (Breakers) ---
	public void UpdateBreakerState(bool isClosed)
	{
		if (_type != NodeType.Digital) return;

		var label = GetNodeOrNull<Label>("ValueLabel"); // Ensure you have a label named ValueLabel on your Digital node
		if (label != null) 
		{
			label.Text = isClosed ? "CLOSED (OK)" : "TRIPPED (ALERT)";
		}

		// Green for normal operation, Red for cyber-kinetic impact
		if (isClosed) 
		{
			PaintTitleBar(new Color(0.1f, 0.8f, 0.1f)); 
		}
		else 
		{
			PaintTitleBar(new Color(0.9f, 0.1f, 0.1f)); 
		}
	}
}
