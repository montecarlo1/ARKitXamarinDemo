﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ARKit;
using Foundation;
using UIKit;
using Urho;
using Urho.Urho2D;

namespace ARKitXamarinDemo
{
	public class ArkitApp : Urho.Application
	{
		Texture2D cameraYtexture;
		Texture2D cameraUVtexture;
		bool yuvTexturesInited;

		[Preserve]
		public ArkitApp(ApplicationOptions opts) : base(opts) { }

		public Viewport Viewport { get; private set; }
		public Scene Scene { get; private set; }
		public Node CameraNode { get; private set; }
		public Camera Camera { get; private set; }
		public Node LightNode { get; private set; }
		public Light Light { get; private set; }
		public ARSession ARSession { get; private set; }

		public void CreateArScene()
		{
			// 3D scene with Octree and Zone
			Scene = new Scene(Context);
			var octree = Scene.CreateComponent<Octree>();
			var zone = Scene.CreateComponent<Zone>();
			zone.AmbientColor = new Color(0.1f, 0.1f, 0.1f);

			// Light
			var lightNode = Scene.CreateChild(name: "DirectionalLight");
			lightNode.SetDirection(new Vector3(0.6f, -1.0f, 0.8f));
			var light = lightNode.CreateComponent<Light>();
			light.LightType = LightType.Directional;
			light.CastShadows = true;
			light.ShadowIntensity = 0.5f;
			light.ShadowCascade = new CascadeParameters(10.0f, 50.0f, 200.0f, 0.0f, 0.8f);

			// Camera
			CameraNode = Scene.CreateChild(name: "Camera");
			Camera = CameraNode.CreateComponent<Camera>();

			// Viewport
			Viewport = new Viewport(Context, Scene, Camera, null);
			Renderer.SetViewport(0, Viewport);
		}

		protected virtual void OnARSessionSet(ARSession session) { }

		public unsafe void ProcessARFrame(ARSession session, ARFrame frame)
		{
			if (ARSession == null)
				OnARSessionSet(ARSession = session);

			var arcamera = frame?.Camera;
			var transform = arcamera.Transform;
			var projection = arcamera.ProjectionMatrix;

			// calculate rotation
			// we use ARCamera.EulerAngles (vec3) but also can use ARCamera.Transform (mat4)
			var ea = arcamera.EulerAngles;
			var rotation = new Quaternion(
				MathHelper.RadiansToDegrees(-ea.X),
				MathHelper.RadiansToDegrees(-ea.Y),
				MathHelper.RadiansToDegrees(ea.Z));

			// extract parameters from Projection Matrix
			float near = projection.M43 / projection.M33;
			float far = projection.M43 / (projection.M33 + 1);
			float aspect = projection.M22 / projection.M11;
			float fovH = 360f * (float)Math.Atan(1f / projection.M11) / MathHelper.Pi;
			float fovV = 360f * (float)Math.Atan(1f / projection.M22) / MathHelper.Pi;
			float projectOffsetX = -projection.M31 / 2f;
			float projectOffsetY = -projection.M32 / 2f;

			//Update data on Update
			//TODO: move Engine.RenderFrame() to DidUpdateFrame callback
			InvokeOnMain(() =>
			{
				// Set Camera parameters
				// NOTE: Do I have to set it each frame?
				Camera.Skew = projection.M21;
				Camera.ProjectionOffset = new Vector2(projectOffsetX, projectOffsetY);
				Camera.AspectRatio = aspect;
				Camera.Fov = fovV;
				Camera.NearClip = near;
				Camera.FarClip = far;

				// Rotation
				CameraNode.Rotation = rotation;

				// Position
				var row = arcamera.Transform.Row3;
				CameraNode.Position = new Vector3(row.X, row.Y, -row.Z);

				if (!yuvTexturesInited)
				{
					var img = frame.CapturedImage;

					// texture for Y-plane;
					cameraYtexture = new Texture2D();
					cameraYtexture.SetNumLevels(1);
					cameraYtexture.FilterMode = TextureFilterMode.Bilinear;
					cameraYtexture.SetAddressMode(TextureCoordinate.U, TextureAddressMode.Clamp);
					cameraYtexture.SetAddressMode(TextureCoordinate.V, TextureAddressMode.Clamp);
					cameraYtexture.SetSize((int)img.Width, (int)img.Height, Graphics.LuminanceFormat, TextureUsage.Dynamic);
					//cameraYtexture.SetSize(Graphics.Width, Graphics.Height, Graphics.LuminanceFormat, TextureUsage.Dynamic);
					cameraYtexture.Name = nameof(cameraYtexture);
					ResourceCache.AddManualResource(cameraYtexture);

					// texture for UV-plane;
					cameraUVtexture = new Texture2D();
					cameraUVtexture.SetNumLevels(1);
					cameraUVtexture.SetSize((int)img.GetWidthOfPlane(1), (int)img.GetHeightOfPlane(1), Graphics.LuminanceAlphaFormat, TextureUsage.Dynamic);
					cameraUVtexture.FilterMode = TextureFilterMode.Bilinear;
					cameraUVtexture.SetAddressMode(TextureCoordinate.U, TextureAddressMode.Clamp);
					cameraUVtexture.SetAddressMode(TextureCoordinate.V, TextureAddressMode.Clamp);
					cameraUVtexture.Name = nameof(cameraUVtexture);
					ResourceCache.AddManualResource(cameraUVtexture);

					RenderPath rp = new RenderPath();
					//rp.SetShaderParameter("CameraScale", (float)Graphics.Width / (int)img.Width); 
					rp.Load(ResourceCache.GetXmlFile("ARRenderPath.xml"));
					var cmd = rp.GetCommand(1); //see ARRenderPath.xml, second command.
					//TextureName0 stands for sDiffMap, TextureName1 stands for sNormalMap
					//TODO: surface RenderPathCommand::SetTexture(TextureAddress, string) method 
					cmd->TextureName0 = ToUrhoString(nameof(cameraYtexture));
					cmd->TextureName1 = ToUrhoString(nameof(cameraUVtexture));
					Viewport.RenderPath = rp;
					yuvTexturesInited = true;
				}

				//use outside of InvokeOnMain?
				if (yuvTexturesInited)
					UpdateBackground(frame);
				// required!
				frame.Dispose();
			});
		}

		unsafe void UpdateBackground(ARFrame frame)
		{
			using (var img = frame.CapturedImage)
			{
				var yPtr = img.BaseAddress;
				var uvPtr = img.GetBaseAddress(1);

				if (yPtr == IntPtr.Zero || uvPtr == IntPtr.Zero)
					return;

				cameraYtexture.SetData(0, 0, 0, (int)img.Width, (int)img.Height, (void*)yPtr);
				cameraUVtexture.SetData(0, 0, 0, (int)img.GetWidthOfPlane(1), (int)img.GetHeightOfPlane(1), (void*)uvPtr);
			}
		}

		//temp workaround, will be fixed in the next UrhoSharp update
		static UrhoString ToUrhoString(string str)
		{
			var us = new UrhoString();
			us.Buffer = Marshal.StringToHGlobalAnsi(str);
			us.Length = (uint)str.Length;
			us.Capacity = (uint)us.Length + 1;
			return us;
		}
	}
}