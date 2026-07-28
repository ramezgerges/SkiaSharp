using SkiaSharp.Tests.Visual;

namespace SkiaSharp.Benchmarks.Rendering;

/// <summary>
/// A scene whose drawing can be split into <see cref="PartitionCount"/>
/// independent sub-drawings, each rendered into a partition of the target
/// surface. Backends that support parallel recording dispatch each partition
/// onto its own worker; sequential backends just call <c>DrawPartition</c> in
/// a loop.
///
/// <para>
/// The partitioning contract: partition <c>i</c>'s drawing is self-contained
/// and Z-orders below partition <c>i+1</c>. Two partitions may draw
/// overlapping pixels — the render thread inserts them in index order, so
/// blending on the destination surface produces the same result as a
/// sequential single-thread render. What partitions may NOT do is read
/// pixels written by another partition (i.e. no backdrop filters that
/// sample the destination across partition boundaries).
/// </para>
///
/// <para>
/// <see cref="ISkiaScene.Draw"/> is implemented by scenes as a straight
/// serial loop over partitions — that's the sequential baseline every
/// non-parallel backend uses.
/// </para>
/// </summary>
public interface IPartitionedSkiaScene : ISkiaScene
{
	/// <summary>Number of independent partitions the scene splits into.</summary>
	int PartitionCount { get; }

	/// <summary>
	/// Draws partition <paramref name="partitionIndex"/> into
	/// <paramref name="canvas"/>. The canvas is expected to be positioned at
	/// the scene's origin — the partition draws in scene-space coordinates,
	/// letting the sequential and parallel dispatch paths share the same
	/// scene code.
	/// </summary>
	void DrawPartition(SKCanvas canvas, int partitionIndex);
}
