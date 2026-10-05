using System.Text.RegularExpressions;

namespace Floatie.Core;

/// <summary>Wildcard filter for file names: "*.pdf;*.doc*;!~$*". Case-insensitive.
/// A name matches when it matches any include pattern and no exclude ("!") pattern.</summary>
public sealed class PatternMatcher
{
    private readonly Regex[] _include;
    private readonly Regex[] _exclude;

    public PatternMatcher(string? patterns)
    {
        var parts = (patterns ?? "*").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _include = parts.Where(p => !p.StartsWith('!')).Select(ToRegex).ToArray();
        _exclude = parts.Where(p => p.StartsWith('!')).Select(p => ToRegex(p[1..])).ToArray();
        if (_include.Length == 0) _include = new[] { ToRegex("*") };
    }

    public bool IsMatch(string fileName) =>
        _include.Any(r => r.IsMatch(fileName)) && !_exclude.Any(r => r.IsMatch(fileName));

    private static Regex ToRegex(string glob) =>
        new("^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>Ready-made filters offered in the fence editor.</summary>
public static class FilterPresets
{
    public static readonly (string Name, string Patterns)[] All =
    {
        ("Everything", "*"),
        ("Documents", "*.pdf;*.doc;*.docx;*.odt;*.rtf;*.txt;*.md;*.xls;*.xlsx;*.csv;*.ods;*.ppt;*.pptx;*.odp"),
        ("Images", "*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg;*.heic;*.tif;*.tiff;*.ico"),
        ("Videos", "*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv"),
        ("Music", "*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg"),
        ("Archives", "*.zip;*.rar;*.7z;*.tar;*.gz;*.bz2;*.xz"),
        ("Apps & shortcuts", "*.lnk;*.url;*.exe;*.appref-ms"),
        ("Code", "*.py;*.ipynb;*.js;*.ts;*.cs;*.c;*.cpp;*.h;*.java;*.json;*.xml;*.yaml;*.yml;*.html;*.css;*.m;*.slx"),
    };
}
