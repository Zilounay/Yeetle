namespace Yeetle.Domain.Clips;

// A labelled moment in a clip (spec §7). Number identifies it within its clip and gives the
// default label "Marker N"; markers are kept in position order.
public sealed record Marker(int Number, TimeSpan Position, string Label);
