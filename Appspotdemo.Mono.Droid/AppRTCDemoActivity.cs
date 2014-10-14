using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Java.Lang;
using Java.Util.Regex;
using Org.Json;
using Org.Webrtc;
using Exception = System.Exception;
using Pattern = Android.OS.Pattern;
using StringBuilder = System.Text.StringBuilder;
using Thread = System.Threading.Thread;
using VideoSource = Android.Media.VideoSource;
using Uri = Android.Net.Uri;

namespace Appspotdemo.Mono.Droid
{
    [Activity(Label = "Appspotdemo.Mono.Droid", MainLauncher = true, Icon = "@drawable/ic_launcher")]

	class AppRTCDemoActivity : Activity, NodeClient.UserMediaObserver, NodeClient.AddStreamObserver
    {
        private bool InstanceFieldsInitialized = false;
        private const string TAG = "AppRTCDemoActivity";
        private NodeClient appRtcClient;
        private VideoStreamsView vsv;
        private Toast logToast;
		private Org.Webrtc.VideoSource videoSource;
		private readonly Boolean[] quit = new Boolean[] { Boolean.False };

		private PeerConnectionFactory factory;
		 
        public AppRTCDemoActivity()
		{
			if (!InstanceFieldsInitialized)
			{
				InitializeInstanceFields();
				InstanceFieldsInitialized = true;
			}
		}

        private void InitializeInstanceFields()
        {
			abortUnless(PeerConnectionFactory.InitializeAndroidGlobals(this), "Failed to initializeAndroidGlobals");
			appRtcClient = new NodeClient(this, this, this);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Java.Lang.Thread.DefaultUncaughtExceptionHandler = new UnhandledExceptionHandler(this);

            Window.AddFlags(WindowManagerFlags.Fullscreen);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            Point displaySize = new Point();
            WindowManager.DefaultDisplay.GetSize(displaySize);
            vsv = new VideoStreamsView(this, displaySize);
            SetContentView(vsv);

            Intent intent = Intent;
            if ("Android.intent.action.VIEW".Equals(intent.Action))
            {
                connectToRoom(intent.Data.ToString());
                return;
            }
            showGetRoomUI();
        }

        private void showGetRoomUI()
        {
            EditText roomInput = new EditText(this);
            roomInput.Text = "http://meeting.appinux.com/?join=";
            roomInput.SetSelection(roomInput.Text.Length);
            IDialogInterfaceOnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this, roomInput);
            AlertDialog.Builder builder = new AlertDialog.Builder(this);
            builder.SetMessage("Enter room URL").SetView(roomInput).SetPositiveButton("Go!", listener).Show();
        }

        private class OnClickListenerAnonymousInnerClassHelper : Java.Lang.Object, IDialogInterfaceOnClickListener
        {
            private readonly AppRTCDemoActivity outerInstance;

            private EditText roomInput;

            public OnClickListenerAnonymousInnerClassHelper(AppRTCDemoActivity outerInstance, EditText roomInput)
            {
                this.outerInstance = outerInstance;
                this.roomInput = roomInput;
            }

            public void OnClick(IDialogInterface dialog, int which)
            {
                dialog.Dismiss();
                outerInstance.connectToRoom(roomInput.Text.ToString());
            }
        }
        
        private void connectToRoom(string roomUrl)
        {
            logAndToast("Connecting to room...");
            appRtcClient.connectToRoom(roomUrl);
        }

        protected override void OnPause()
        {
            base.OnPause();
            vsv.OnPause();
        }

		public void onUserMediaObserver (JSONObject constraints)
		{
			Log.Debug(TAG, "Creating local video source...");

			factory = new PeerConnectionFactory();

			MediaStream lMS = factory.CreateLocalMediaStream("ARDAMS");
			if (constraints.Has ("video")) {
				VideoCapturer capturer = VideoCapturer;
				videoSource = factory.CreateVideoSource(capturer, appRtcClient.VideoConstrants);
				VideoTrack videoTrack = factory.CreateVideoTrack("ARDAMSv0", videoSource);
				videoTrack.AddRenderer(new VideoRenderer(new VideoCallbacks(this, vsv, VideoStreamsView.Endpoint.LOCAL)));
				lMS.AddTrack(videoTrack);
			}
			lMS.AddTrack(factory.CreateAudioTrack("ARDAMSa0"));

			Log.Debug(TAG, "Waiting for ICE candidates...");

			appRtcClient.onStream (factory, lMS);
		}

		public void onAddStreamObserver (MediaStream stream)
		{
			//RunOnUiThread(() =>
			//{
			//    abortUnless(stream.AudioTracks.Size() <= 1 && stream.VideoTracks.Size() <= 1, "Weird-looking stream: " + stream);
			//    if (stream.VideoTracks.Size() == 1)
			//    {
			//        ((Org.Webrtc.VideoTrack)stream.VideoTracks.Get(0)).AddRenderer(new VideoRenderer(new VideoCallbacks(this, this.vsv, VideoStreamsView.Endpoint.REMOTE)));
			//    }
				// });		
		}

 		// Implementation detail: bridge the VideoRenderer.Callbacks interface to the
		// VideoStreamsView implementation.
		public class VideoCallbacks : Java.Lang.Object, VideoRenderer.ICallbacks
		{
			private readonly AppRTCDemoActivity outerInstance;

			internal readonly VideoStreamsView view;
			internal readonly VideoStreamsView.Endpoint stream;

			public VideoCallbacks(AppRTCDemoActivity outerInstance, VideoStreamsView view, VideoStreamsView.Endpoint stream)
			{
				this.outerInstance = outerInstance;
				this.view = view;
				this.stream = stream;
			}

			//JAVA TO C# CONVERTER WARNING: 'final' parameters are not allowed in .NET:
			//ORIGINAL LINE: @Override public void setSize(final int width, final int height)
			public void SetSize(int width, int height)
			{
				view.QueueEvent(() => view.setSize(stream, width, height));
			}

			public void RenderFrame(VideoRenderer.I420Frame frame)
			{
				view.queueFrame(stream, frame);
			}
		}

		// Cycle through likely device names for the camera and return the first
		// capturer that works, or crash if none do.
		private VideoCapturer VideoCapturer
		{
			get
			{
				string[] cameraFacing = new string[] { "front", "back" };
				int[] cameraIndex = new int[] { 0, 1 };
				int[] cameraOrientation = new int[] { 0, 90, 180, 270 };
				foreach (string facing in cameraFacing)
				{
					foreach (int index in cameraIndex)
					{
						foreach (int orientation in cameraOrientation)
						{
							string name = "Camera " + index + ", Facing " + facing + ", Orientation " + orientation;
							VideoCapturer capturer = VideoCapturer.Create(name);
							if (capturer != null)
							{
								Log.Debug (TAG, "Using camera: " + name);
								return capturer;
							}
						}
					}
				}
				throw new Exception("Failed to open capturer");
			}
		}

        protected override void OnDestroy()
        {
             base.OnDestroy();
        }

        private static void abortUnless(bool condition, string msg)
        {
            if (!condition)
            {
                throw new Exception(msg);
            }
        }

        private void logAndToast(string msg)
        {
            Log.Debug(TAG, msg);
            if (logToast != null)
            {
                logToast.Cancel();
            }
            logToast = Toast.MakeText(this, msg, ToastLength.Short);
            logToast.Show();
        }

		private static void jsonPut(JSONObject json, string key, Java.Lang.Object value)
		{
			try
			{
				json.Put(key, value);
			}
			catch (JSONException e)
			{
				throw new Exception("Error", e);
			}
		}
    }
}