using Calcpad.OpenXml;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Calcpad
{
    internal class Converter
    {
        private readonly StringBuilder _sb = new();
        private readonly string _htmlWorksheet;
        private static readonly char _dirSeparator = Path.DirectorySeparatorChar;

        internal Converter()
        {
            var appUrl = $"file:///{Program.AppPath.Replace("\\", "/")}/doc/";
            var templatePath =  $"{Program.AppPath}{_dirSeparator}doc{_dirSeparator}template{Program.AddCultureExt("html")}";
            _htmlWorksheet = File.ReadAllText(templatePath).Replace("jquery", appUrl + "jquery");
        }

        internal void ToHtml(string html, string path)
        {
            File.WriteAllText(path, HtmlApplyWorksheet(html));
        }

        internal void ToOpenXml(string html, string path, List<string> expressions)
        {
            html = GetHtmlData(HtmlApplyWorksheet(html));
            new OpenXmlWriter(expressions).Convert(html, path);
        }
        internal string HtmlApplyWorksheet(string s)
        {
            _sb.Append(_htmlWorksheet);
            _sb.Append(s);
            _sb.Append(" </body></html>");
            return _sb.ToString();
        }

        private static string GetHtmlData(string html)
        {
            var sb = new StringBuilder(500);
            const string header =
@"Version:1.0
StartHTML:0000000001
EndHTML:0000000002
StartFragment:0000000003
EndFragment:0000000004";
            const string startFragmentText = "<!DOCTYPE HTML><!--StartFragment-->";
            const string endFragmentText = "<!--EndFragment-->";
            var startHtml = header.Length;
            var startFragment = startHtml + startFragmentText.Length;
            var endFragment = startFragment + html.Length;
            var endHtml = endFragment + endFragmentText.Length;
            sb.Append(header);
            sb.Replace("0000000001", $"{startHtml,8}");
            sb.Replace("0000000002", $"{endHtml,8}");
            sb.Replace("0000000003", $"{startFragment,8}");
            sb.Replace("0000000004", $"{endFragment,8}");
            sb.Append(startFragmentText);
            sb.Append(html);
            sb.Append(endFragmentText);
            return sb.ToString();
        }
    }
}
