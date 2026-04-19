namespace FitTrackr.MAUI.Models.DTO
{
    public class ExerciseCatalogItemDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string BodyPart { get; set; } = string.Empty;

        public string Equipment { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string ImageName { get; set; } = string.Empty;
    }
}
