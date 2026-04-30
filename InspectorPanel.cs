using Godot;
using System;
using System.Text.RegularExpressions;

public partial class InspectorPanel : PanelContainer
{
	// --- UI REFERENCES ---
	private Label _title;
	private LineEdit _nameField;
	private OptionButton _typeSelector;
	private OptionButton _purdueSelector;
	private OptionButton _roleSelector;
	private LineEdit _ipField;
	private Button _terminalButton; 

	// --- Network Links UI References ---
	private VBoxContainer _linksContainer; 
	private GraphEdit _parentGraph;        

	// --- STATE ---
	private AssetNode _currentNode;
	private bool _networkReady = false; 
	private bool _isInternalUpdate = false; 

	// ✅ FIXED ROLE MAPPING (matches Python)
	private readonly string[] _roleTags = { "Standard", "RedTeam", "BlueTeam", "Adversary" };

	public override void _Ready()
	{
		var container = GetNode<VBoxContainer>("VBoxContainer");
		
		_title = container.GetNode<Label>("TitleLabel");
		_nameField = GetOrCreateChild<LineEdit>(container, "NameField", 1);
		_typeSelector = container.GetNode<OptionButton>("TypeSelector");
		_purdueSelector = GetOrCreateChild<OptionButton>(container, "PurdueSelector", 3);
		_roleSelector = GetOrCreateChild<OptionButton>(container, "RoleSelector", 4);
		_ipField = container.GetNode<LineEdit>("IpField");

		_terminalButton = new Button();
		_terminalButton.Name = "TerminalButton";
		_terminalButton.Text = ">_ Open Terminal";
		_terminalButton.IconAlignment = HorizontalAlignment.Center;
		_terminalButton.Disabled = true; 
		container.AddChild(_terminalButton);

		// --- SETUP LINKS UI CONTAINER ---
		var linkHeader = new Label();
		linkHeader.Text = "--- Network Interfaces ---";
		linkHeader.HorizontalAlignment = HorizontalAlignment.Center;
		linkHeader.AddThemeConstantOverride("margin_top", 15); 
		container.AddChild(linkHeader);

		_linksContainer = new VBoxContainer();
		container.AddChild(_linksContainer);

		// --- POPULATE DROPDOWNS ---
		_typeSelector.Clear();
		foreach (string typeName in Enum.GetNames(typeof(AssetNode.NodeType)))
			_typeSelector.AddItem(typeName);

		_purdueSelector.Clear();
		foreach (string layer in Enum.GetNames(typeof(AssetNode.PurdueLayer)))
			_purdueSelector.AddItem(layer.Replace("_", " "));

		_roleSelector.Clear();
		_roleSelector.AddItem("Standard / None", 0);
		_roleSelector.AddItem("Red Team (Kali)", 1);
		_roleSelector.AddItem("Blue Team (Workstation)", 2);
		_roleSelector.AddItem("Automated Adversary (CALDERA)", 3);
		
		_nameField.PlaceholderText = "Host Name (e.g. PLC_01)";

		// --- SIGNALS ---
		_nameField.TextChanged += OnNameChanged;
		_ipField.TextChanged += OnIpChanged;
		_typeSelector.ItemSelected += OnTypeChanged;
		_purdueSelector.ItemSelected += OnPurdueChanged; 
		_roleSelector.ItemSelected += OnRoleChanged;
		_terminalButton.Pressed += OnTerminalPressed;

		Visible = false;
	}

	private T GetOrCreateChild<T>(Node parent, string name, int index) where T : Node, new()
	{
		var node = parent.GetNodeOrNull<T>(name);
		if (node == null)
		{
			node = new T();
			node.Name = name;
			parent.AddChild(node);
			parent.MoveChild(node, index);
		}
		return node;
	}

	public void SetNetworkReady(bool ready)
	{
		_networkReady = ready;
		if (_currentNode != null) UpdateUiVisibility(_currentNode.Type);
	}

	// --- LOAD DATA ---
	public void LoadNode(AssetNode node)
	{
		if (node == null) return;

		_currentNode = null; 
		_isInternalUpdate = true;
		_currentNode = node;
		
		Visible = true;
		
		_title.Text = "Editing ID: " + node.Name;
		_nameField.Text = node.Metadata.ContainsKey("name") ? node.Metadata["name"] : "";
		_ipField.Text = node.Metadata.ContainsKey("ip") ? node.Metadata["ip"] : "";

		_typeSelector.Select((int)node.Type);
		_purdueSelector.Select((int)node.CurrentPurdueLayer);

		int roleIndex = 0; 
		if (node.Metadata.ContainsKey("Role"))
		{
			roleIndex = Array.IndexOf(_roleTags, node.Metadata["Role"]);
			if (roleIndex == -1) roleIndex = 0; 
		}
		_roleSelector.Select(roleIndex);
			
		UpdateUiVisibility(node.Type);
		
		// --- POPULATE THE WIRES UI ---
		PopulateLinksUI(node);

		_isInternalUpdate = false;
	}

	public void ClearSelection() 
	{ 
		_currentNode = null; 
		Visible = false; 
	}

	private void UpdateUiVisibility(AssetNode.NodeType type)
	{
		_terminalButton.Visible = true;
		_terminalButton.Disabled = !_networkReady;

		// ✅ FIXED: removed invalid enum reference
		bool canHaveRole = (type == AssetNode.NodeType.Digital || type == AssetNode.NodeType.HMI);
		_roleSelector.Disabled = !canHaveRole;

		if (!canHaveRole && _roleSelector.Selected != 0)
		{
			_roleSelector.Selected = 0; 
			if (_currentNode != null && !_isInternalUpdate)
			{
				_currentNode.UpdateData("Role", "Standard");
			}
		}
	}

	// --- NETWORK INTERFACES GENERATOR ---
	private void PopulateLinksUI(AssetNode node)
	{
		foreach (Node child in _linksContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_parentGraph == null) _parentGraph = node.GetParent<GraphEdit>();
		if (_parentGraph == null) return;

		bool hasLinks = false;

		foreach (var connection in _parentGraph.GetConnectionList())
		{
			string fromNode = connection["from_node"].AsString();
			string toNode = connection["to_node"].AsString();

			if (fromNode == node.Name || toNode == node.Name)
			{
				hasLinks = true;
				Label linkLabel = new Label();
				
				if (fromNode == node.Name)
					linkLabel.Text = $"🔌 [OUT] to {toNode}";
				else
					linkLabel.Text = $"🔌 [IN] from {fromNode}";
				
				linkLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
				_linksContainer.AddChild(linkLabel);
			}
		}

		if (!hasLinks)
		{
			Label emptyLabel = new Label();
			emptyLabel.Text = "No active connections.";
			emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			_linksContainer.AddChild(emptyLabel);
		}
	}

	// --- INPUT EVENT HANDLERS ---
	private void OnTypeChanged(long index)
	{
		if (_currentNode == null || _isInternalUpdate) return;
		
		_currentNode.Type = (AssetNode.NodeType)index;
		_purdueSelector.Select((int)_currentNode.CurrentPurdueLayer);
		UpdateUiVisibility(_currentNode.Type);
	}

	private void OnPurdueChanged(long index)
	{
		if (_currentNode == null || _isInternalUpdate) return;
		_currentNode.CurrentPurdueLayer = (AssetNode.PurdueLayer)index;
		_currentNode.UpdateVisuals(); 
	}

	private void OnRoleChanged(long index) 
	{ 
		 if (_currentNode == null || _isInternalUpdate) return;
		 string roleTag = _roleTags[(int)index];
		 _currentNode.UpdateData("Role", roleTag);
	}
	
	private void OnNameChanged(string newText) 
	{ 
		if (_currentNode != null && !_isInternalUpdate) 
		{
			string cleanName = newText.Trim(); 
			_currentNode.UpdateData("name", cleanName);
			_currentNode.AssetName = cleanName; 
			_currentNode.UpdateVisuals();
			PopulateLinksUI(_currentNode); 
		}
	}

	private void OnIpChanged(string newText) 
	{ 
		if (_currentNode != null && !_isInternalUpdate) 
			_currentNode.UpdateData("ip", newText.Trim()); 
	}

	// --- TERMINAL LOGIC ---
	private string GetExpectedDockerName(AssetNode node)
	{
		string customName = "";
		if (node.Metadata.ContainsKey("name"))
		{
			customName = node.Metadata["name"].Trim();
		}

		string baseName = !string.IsNullOrEmpty(customName) ? customName : $"{node.Type}_{node.Name}";

		string cleaned = Regex.Replace(baseName, @"[^a-zA-Z0-9_.-]", "_");
		cleaned = cleaned.Trim('_', '-').ToLower();

		return cleaned;
	}

	private void OnTerminalPressed()
	{
		if (_currentNode == null) return;

		string containerName = GetExpectedDockerName(_currentNode);
		GD.Print($"Attempting to open terminal for: {containerName}");

		string shell = "/bin/sh"; 

		string dockerCmd = $"docker exec -it {containerName} {shell} || read -p 'Container {containerName} not found. Press Enter...'";

		try 
		{
			if (OS.GetName() == "Linux" || OS.GetName() == "FreeBSD")
			{
				OS.Execute("gnome-terminal", new string[] { "--", "bash", "-c", dockerCmd });
			}
			else
			{
				GD.PrintErr("Terminal auto-launch is currently only supported on Linux via gnome-terminal.");
				GD.Print($"Manual command: {dockerCmd}");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to launch terminal: {e.Message}");
		}
	}
}
