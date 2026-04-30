using System.Collections.Generic;

public static class LinkDataStore
{
	// Key: "FromNodeName_ToNodeName", Value: "Bidirectional" or "Unidirectional"
	public static Dictionary<string, string> LinkTypes = new Dictionary<string, string>();

	public static string GetLinkId(string from, string to)
	{
		return $"{from}__{to}";
	}

	// Default to Bidirectional when a new connection is dragged
	public static void SetLinkType(string from, string to, string type)
	{
		LinkTypes[GetLinkId(from, to)] = type;
	}

	public static string GetLinkType(string from, string to)
	{
		string id = GetLinkId(from, to);
		return LinkTypes.ContainsKey(id) ? LinkTypes[id] : "Bidirectional";
	}

	public static void RemoveLink(string from, string to)
	{
		LinkTypes.Remove(GetLinkId(from, to));
	}
}
