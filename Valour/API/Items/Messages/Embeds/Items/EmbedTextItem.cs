﻿namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedTextItem : EmbedItem
{
    public string? Name { get; set; }
    public string? TextColor { get; set; } = "eeeeee";
    public string? Link { get; set; }
    public string? Text { get; set; }

    /// <summary>
    /// The page number that the embed will be set to when a user clicks this text
    /// </summary>
    public int? GoToPage { get; set; }

    public EmbedTextItem()
    {
        ItemType = EmbedItemType.Text;
    }

    public EmbedTextItem(string name = null, string text = null, string textColor = null, string link = null, int? x = null, int? y = null)
    {
        Name = name;
        Text = text;
        TextColor = textColor;
        Link = link;
        X = x;
        Y = y;
        ItemType = EmbedItemType.Text;
    }
}