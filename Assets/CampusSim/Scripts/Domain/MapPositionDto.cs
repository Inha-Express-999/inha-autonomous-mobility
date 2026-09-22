namespace InhaExpress.Client.Domain
{
    // Local metric coordinates: east, north, height. Negative coordinates are valid.
    public readonly struct MapPositionDto
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public MapPositionDto(double x, double y, double z)
        {
            X = DtoGuard.Finite(x, nameof(x));
            Y = DtoGuard.Finite(y, nameof(y));
            Z = DtoGuard.Finite(z, nameof(z));
        }
    }
}
