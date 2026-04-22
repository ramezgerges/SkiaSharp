using System;
using System.IO;

using Xunit;

namespace HarfBuzzSharp.Tests
{
	public class HBFaceTest : HBTest
	{
		// Variable font helpers
		private Face CreateVariableFace ()
		{
			using var blob = Blob.FromFile (Path.Combine (PathToFonts, "Distortable.ttf"));
			return new Face (blob, 0);
		}

		// US1: Query Variable Font Axes

		[SkippableFact]
		public void CanGetVariationAxisCount ()
		{
			using var face = CreateVariableFace ();
			Assert.True (face.GetVariationAxisCount () > 0);
		}

		[SkippableFact]
		public void CanGetVariationAxisInfos ()
		{
			using var face = CreateVariableFace ();
			var axes = face.GetVariationAxisInfos ();
			Assert.NotEmpty (axes);

			// Distortable.ttf should have at least one axis
			var axis = axes[0];
			Assert.NotEqual ((uint)0, axis.Tag);
			Assert.True (axis.MinValue <= axis.DefaultValue);
			Assert.True (axis.DefaultValue <= axis.MaxValue);
		}

		[SkippableFact]
		public void VariationAxisCountIsZeroForStaticFont ()
		{
			using var face = new Face (Blob, 0);
			Assert.Equal (0, face.GetVariationAxisCount ());
		}

		[SkippableFact]
		public void TryFindVariationAxisReturnsTrueForExistingAxis ()
		{
			using var face = CreateVariableFace ();
			var axes = face.GetVariationAxisInfos ();
			Assert.NotEmpty (axes);

			Tag axisTag = axes[0].Tag;
			var found = face.TryFindVariationAxis (axisTag, out var axisInfo);
			Assert.True (found);
			Assert.Equal (axes[0].Tag, axisInfo.Tag);
		}

		[SkippableFact]
		public void TryFindVariationAxisReturnsFalseForMissingAxis ()
		{
			using var face = CreateVariableFace ();
			// Use a tag that is unlikely to exist
			var found = face.TryFindVariationAxis (Tag.Parse ("zzzz"), out _);
			Assert.False (found);
		}

		// US3: Named Instances

		[SkippableFact]
		public void CanGetNamedInstanceCount ()
		{
			using var face = CreateVariableFace ();
			var count = face.GetNamedInstanceCount ();
			// Distortable.ttf may or may not have named instances, but the call should not throw
			Assert.True (count >= 0);
		}

		[SkippableFact]
		public void CanGetNamedInstanceDesignCoords ()
		{
			using var face = CreateVariableFace ();
			var count = face.GetNamedInstanceCount ();
			if (count == 0)
				return; // Font has no named instances, skip

			var coords = face.GetNamedInstanceDesignCoords (0);
			Assert.NotNull (coords);
		}

		[SkippableFact]
		public void NamedInstanceCountIsZeroForStaticFont ()
		{
			using var face = new Face (Blob, 0);
			Assert.Equal (0, face.GetNamedInstanceCount ());
		}

		[SkippableFact]
		public void HasVariationDataIsTrueForVariableFont ()
		{
			using var face = CreateVariableFace ();
			Assert.True (face.HasVariationData);
		}

		[SkippableFact]
		public void HasVariationDataIsFalseForStaticFont ()
		{
			using var face = new Face (Blob, 0);
			Assert.False (face.HasVariationData);
		}

		[SkippableFact]
		public void ShouldHaveGlyphCount()
		{
			using (var face = new Face(Blob, 0))
			{
				Assert.Equal(1147, face.GlyphCount);
			}
		}

		[SkippableFact]
		public void ShouldBeImmutable()
		{
			using (var face = new Face(Blob, 0))
			{
				face.MakeImmutable();

				Assert.True(face.IsImmutable);
			}
		}

		[SkippableFact]
		public void ShouldHaveIndex()
		{
			using (var face = new Face(Blob, 0))
			{
				Assert.Equal(0, face.Index);
			}
		}

		[SkippableFact]
		public void ShouldHaveUnitsPerEm()
		{
			using (var face = new Face(Blob, 0))
			{
				Assert.Equal(2048, face.UnitsPerEm);
			}
		}

		[SkippableFact]
		public void ShouldHaveTableTags()
		{
			using (var face = new Face(Blob, 0))
			{
				Assert.Equal(20, face.Tables.Length);
			}
		}

		[SkippableFact]
		public void ShouldReferenceTable()
		{
			using (var face = new Face(Blob, 0))
			using (var tableBlob = face.ReferenceTable(Tag.Parse("post")))
			{
				Assert.Equal(13378, tableBlob.Length);
			}
		}

		[SkippableFact]
		public void ShouldCreateWithTableFunc()
		{
			var tag = Tag.Parse("kern");

			using (var face = new Face((f, t) => Face.ReferenceTable(t)))
			{
				var blob = face.ReferenceTable(tag);

				Assert.Equal(Face.ReferenceTable(tag).Handle, blob.Handle);
			}
		}

		[SkippableFact]
		public void DelegateBasedConstructionSucceededs()
		{
			var didReference = 0;
			var didDestroy = 0;

			var tag = Tag.Parse("kern");

			Face face = null;
			face = new Face(
				(f, t) =>
				{
					Assert.Equal(face, f);

					didReference++;
					return Face.ReferenceTable(t);
				},
				() =>
				{
					didDestroy++;
				});

			Assert.Equal(0, didReference);
			Assert.Equal(0, didDestroy);

			var blob1 = face.ReferenceTable(tag);

			Assert.Equal(1, didReference);
			Assert.Equal(0, didDestroy);
			Assert.NotNull(blob1);

			var blob2 = face.ReferenceTable(tag);

			Assert.Equal(2, didReference);
			Assert.Equal(0, didDestroy);
			Assert.NotNull(blob2);

			Assert.Equal(blob1.Handle, blob2.Handle);

			face.Dispose();

			Assert.Equal(2, didReference);
			Assert.Equal(1, didDestroy);
		}


		[SkippableFact]
		public void EmptyFacesAreExactlyTheSameInstance()
		{
			var emptyFace1 = Face.Empty;
			var emptyFace2 = Face.Empty;

			Assert.Equal(emptyFace1, emptyFace2);
			Assert.Equal(emptyFace1.Handle, emptyFace2.Handle);
			Assert.Same(emptyFace1, emptyFace2);
		}

		[SkippableFact]
		public void EmptyFacesAreNotDisposed()
		{
			var emptyFace = Face.Empty;
			emptyFace.Dispose();

			Assert.False(emptyFace.IsDisposed());
			Assert.NotEqual(IntPtr.Zero, emptyFace.Handle);
		}
	}
}
