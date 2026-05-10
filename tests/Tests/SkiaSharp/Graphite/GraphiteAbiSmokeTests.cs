using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SkiaSharp.Tests.Graphite
{
	/// <summary>
	/// FR-013 / SC-003 smoke: this feature MUST NOT modify, remove, or change
	/// the behavior of any existing public Ganesh-related API. We can't easily
	/// run a full <c>api-tools diff</c> against the pre-feature baseline in
	/// this test process, so this is a focused spot-check of the most
	/// load-bearing existing members. If any of these assertions fail, a GR*
	/// / SK* member was renamed, removed, or had its signature changed —
	/// ABI break. Investigate before merging.
	///
	/// Add assertions here whenever a new GR*/SK* member becomes "load
	/// bearing" — large downstream surface area or used by every consumer.
	/// </summary>
	public class GraphiteAbiSmokeTests
	{
		[Fact]
		public void GRContext_CreateVulkan_StillExists ()
			=> AssertMethod (typeof (GRContext), "CreateVulkan",
				new[] { typeof (GRVkBackendContext) }, returns: typeof (GRContext));

		[Fact]
		public void GRContext_CreateGl_StillExists ()
			=> AssertMethod (typeof (GRContext), "CreateGl",
				Type.EmptyTypes, returns: typeof (GRContext));

		[Fact]
		public void GRBackendTexture_HasUnchangedShape ()
		{
			var t = typeof (GRBackendTexture);
			Assert.True (t.IsPublic);
			Assert.True (typeof (IDisposable).IsAssignableFrom (t));
		}

		[Fact]
		public void GRVkBackendContext_HasUnchangedShape ()
		{
			var t = typeof (GRVkBackendContext);
			Assert.True (t.IsPublic);
			Assert.True (typeof (IDisposable).IsAssignableFrom (t));
			AssertProperty (t, "VkInstance",         typeof (IntPtr));
			AssertProperty (t, "VkPhysicalDevice",   typeof (IntPtr));
			AssertProperty (t, "VkDevice",           typeof (IntPtr));
			AssertProperty (t, "VkQueue",            typeof (IntPtr));
			AssertProperty (t, "GraphicsQueueIndex", typeof (uint));
			AssertProperty (t, "GetProcedureAddress", typeof (GRVkGetProcedureAddressDelegate));
		}

		[Fact]
		public void SKSurface_RasterFactory_StillExists ()
			=> AssertMethod (typeof (SKSurface), "Create",
				new[] { typeof (SKImageInfo) }, returns: typeof (SKSurface));

		[Fact]
		public void SKSurface_GaneshRenderTargetFactory_StillExists ()
			=> AssertMethod (typeof (SKSurface), "Create",
				new[] { typeof (GRRecordingContext), typeof (bool), typeof (SKImageInfo) },
				returns: typeof (SKSurface));

		[Fact]
		public void SKCanvas_DrawPath_StillExists ()
			=> AssertMethod (typeof (SKCanvas), "DrawPath",
				new[] { typeof (SKPath), typeof (SKPaint) }, returns: typeof (void));

		[Fact]
		public void SKImage_FromEncodedData_StillExists ()
			=> AssertMethod (typeof (SKImage), "FromEncodedData",
				new[] { typeof (byte[]) }, returns: typeof (SKImage));

		[Fact]
		public void SKPaint_Color_PropertyStillExists ()
			=> AssertProperty (typeof (SKPaint), "Color", typeof (SKColor));

		// ---- helpers ----

		private static void AssertMethod (Type type, string name, Type[] parameters, Type returns)
		{
			var method = type.GetMethod (name,
				BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
				binder: null, types: parameters, modifiers: null);
			Assert.True (method != null,
				$"ABI break: {type.FullName}.{name}({string.Join (", ", parameters.Select (p => p.Name))}) is missing.");
			Assert.True (method.ReturnType == returns,
				$"ABI break: {type.FullName}.{name} return type changed: was {returns.Name}, now {method.ReturnType.Name}.");
		}

		private static void AssertProperty (Type type, string name, Type propertyType)
		{
			var prop = type.GetProperty (name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
			Assert.True (prop != null, $"ABI break: {type.FullName}.{name} property is missing.");
			Assert.True (prop.PropertyType == propertyType,
				$"ABI break: {type.FullName}.{name} property type changed: was {propertyType.Name}, now {prop.PropertyType.Name}.");
		}
	}
}
