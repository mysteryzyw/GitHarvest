namespace GitHarvest.Core.Export;

/// <summary>
/// Windows 文件名 / 目录名的规则常量与判定：<see cref="ExportFolderName"/>（校验用户编辑的
/// 目录名）与 <see cref="FileSystemConflictScanner"/>（扫描即将写出的路径）消费同一份规则——
/// 两处各写一份「保留设备名」「非法字符」迟早会漏改一处，规则只有一个真相源。
/// </summary>
internal static class WindowsNameRules
{
    /// <summary>Win32 不允许出现在文件名里的字符（不含路径分隔符：分隔符由各自的语义单独把关）。</summary>
    public static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>
    /// Win32 保留的设备名：这些名字（含带扩展名的形态）在 Windows 上无法创建。
    /// 比较时忽略大小写，与文件系统的语义一致。
    /// </summary>
    public static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>名字是否以点或空格结尾（Windows 会静默去掉它们，实际名字与预期不一致）。</summary>
    public static bool EndsWithDotOrSpace(string name) => name.EndsWith('.') || name.EndsWith(' ');

    /// <summary>名字（去掉扩展名后）是否是 Windows 保留设备名。</summary>
    public static bool IsReservedName(string name)
        => ReservedNames.Any(reserved => string.Equals(reserved, name.Split('.')[0], StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 找出文本里第一个 Windows 不允许的字符。
    /// </summary>
    /// <param name="text">待检查的文本（一段名字或一整条相对路径）。</param>
    /// <param name="treatPathSeparatorsAsInvalid">
    /// 是否把正斜杠也算作非法字符：校验「单层目录名」时为真（名字里出现分隔符会写到输出路径之外）；
    /// 检查整条相对路径时为假——那时 <c>/</c> 正是 git 用的路径分隔符。反斜杠两种情况都非法。
    /// </param>
    /// <param name="description">找到时给出可读描述（控制字符用码位，其余用带引号的字符）。</param>
    public static bool TryFindInvalidCharacter(string text, bool treatPathSeparatorsAsInvalid, out string description)
    {
        foreach (var character in text)
        {
            if (character < ' ')
            {
                description = $"控制字符 U+{(int)character:X4}";
                return true;
            }

            if (InvalidCharacters.Contains(character)
                || character == '\\'
                || (treatPathSeparatorsAsInvalid && character == '/'))
            {
                description = $"「{character}」";
                return true;
            }
        }

        description = string.Empty;
        return false;
    }
}
