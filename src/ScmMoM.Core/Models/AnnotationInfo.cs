namespace ScmMoM.Core.Models;

public class AnnotationInfo
{
    public string CheckRunName { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}
