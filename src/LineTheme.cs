using System;

namespace ScreenLine
{
    internal sealed class LineTheme
    {
        public readonly string Id, Name, Description;
        public readonly string RopeColor, RopeHighlight;
        public readonly double RopeWidth;
        public readonly string[] ClipColors;

        LineTheme(string id, string name, string description, string rope, string highlight, double width, params string[] colors)
        {
            Id = id; Name = name; Description = description;
            RopeColor = rope; RopeHighlight = highlight; RopeWidth = width; ClipColors = colors;
        }

        public static readonly LineTheme[] All = {
            new LineTheme("classic", "经典银灰", "细灰钢索 · 银灰窄夹", "#8A96A7", "#C8CFD8", 1.6, "#DCE3EC"),
            new LineTheme("linen", "棉麻木夹", "天然棉绳 · 浅榉木夹", "#BAA58A", "#EBDFC9", 2.3, "#DFC5A3"),
            new LineTheme("porcelain", "奶白轻盈", "白色编织线 · 奶白夹", "#AEB1B0", "#F3F0E8", 1.9, "#F3F0E8"),
            new LineTheme("color", "柔彩日常", "浅灰棉线 · 柔彩夹", "#A8ACAA", "#DADCD8", 1.9, "#A6BBA1", "#DCAAAA", "#F1D490"),
            new LineTheme("graphite", "石墨金属", "炭灰钢索 · 石墨窄夹", "#565D64", "#949BA3", 1.65, "#575E66")
        };

        public static LineTheme FromId(string id)
        {
            return Array.Find(All, theme => theme.Id == id) ?? All[0];
        }
    }
}
