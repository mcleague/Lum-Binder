using System.Text.Json.Serialization;

namespace BinderApp.Models
{
    public sealed class WorkshopItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;

        [JsonIgnore]
        public string ImageUrl { get; set; } = string.Empty;
    }

    public sealed class WorkshopPackage
    {
        public System.Collections.ObjectModel.ObservableCollection<Tab> Tabs { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<Bind> Binds { get; set; } = new();
    }
}
