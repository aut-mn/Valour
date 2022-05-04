using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedPlanetChannel
{
    ulong Planet_Id { get; set; }
    ulong? Parent_Id { get; set; }

    // Inherited from ISharedChannel
    ulong Id { get; set; }
    string Name { get; set; }
    int Position { get; set; }
    string Description { get; set; }
}