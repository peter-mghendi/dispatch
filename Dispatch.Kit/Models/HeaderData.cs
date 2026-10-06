namespace Dispatch.Kit.Models;

public sealed class HeaderData(string name, string value)
{
    public string Name { get; set; } = name;
    public string Value { get; set; } = value;
}