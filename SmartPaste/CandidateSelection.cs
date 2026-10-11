namespace SmartPaste;

internal sealed class CandidateSelection
{
    public const int Maximum = 3;
    private readonly List<string> items = [];
    public IReadOnlyList<string> Items => items.AsReadOnly();
    public int IndexOf(string candidate) => items.FindIndex(item => string.Equals(item, candidate, StringComparison.OrdinalIgnoreCase));
    public bool Toggle(string candidate)
    {
        int index = IndexOf(candidate);
        if (index >= 0) { items.RemoveAt(index); return true; }
        if (items.Count == Maximum || string.IsNullOrWhiteSpace(candidate)) return false;
        items.Add(candidate); return true;
    }
    public void Remove(int index) { if (index >= 0 && index < items.Count) items.RemoveAt(index); }
    public void Move(int index, int offset)
    {
        int destination = index + offset;
        if (index < 0 || index >= items.Count || destination < 0 || destination >= items.Count) return;
        (items[index], items[destination]) = (items[destination], items[index]);
    }
    public string Join(string separator) => string.Join(separator, items);
}
