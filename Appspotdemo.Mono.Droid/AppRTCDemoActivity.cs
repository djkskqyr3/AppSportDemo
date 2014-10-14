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
using VideoTrack = Org.Webrtc.VideoTrack;

namespace Appspotdemo.Mono.Droid
{
    [Activity(Label = "Appspotdemo.Mono.Droid", MainLauncher = true, Icon = "@drawable/ic_launcher")]

	class AppRTCDemoActivity : Activity, NodeClient.NodeClientObserver
    {
		private AppRTCDemoActivity outerInstance;
        private bool InstanceFieldsInitialized = false;
        private const string TAG = "AppRTCDemoActivity";
        private NodeClient appRtcClient;
        private VideoStreamsView vsv;
		private readonly Boolean[] quit = new Boolean[] { Boolean.False };

		Button btnCreate;
		Button btnJoin;
		Button btnHangup;
		 
        public AppRTCDemoActivity()
		{
			if (!InstanceFieldsInitialized)
			{
				InitializeInstanceFields();
				InstanceFieldsInitialized = true;
				outerInstance = this;
			}
		}

        private void InitializeInstanceFields()
        {
			abortUnless(PeerConnectionFactory.InitializeAndroidGlobals(this), "Failed to initializeAndroidGlobals");
			appRtcClient = new NodeClient(this, this);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Java.Lang.Thread.DefaultUncaughtExceptionHandler = new UnhandledExceptionHandler(this);

            Window.AddFlags(WindowManagerFlags.Fullscreen);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

			Point displaySize = new Point();
			WindowManager.DefaultDisplay.GetSize(displaySize);

			// Creating a new RelativeLayout
			RelativeLayout relativeLayout = new RelativeLayout(this);
			relativeLayout.SetBackgroundColor (Color.White);

			// Defining the RelativeLayout layout parameters.
			// In this case I want to fill its parent
			RelativeLayout.LayoutParams rlp = new RelativeLayout.LayoutParams(
				RelativeLayout.LayoutParams.FillParent,
				RelativeLayout.LayoutParams.FillParent);

			// Create, Join, Hangup buttons layout
			LinearLayout linerLayoutH = new LinearLayout(this);
			linerLayoutH.Orientation = Android.Widget.Orientation.Horizontal;

			LinearLayout.LayoutParams param = new LinearLayout.LayoutParams(
				LinearLayout.LayoutParams.WrapContent, LinearLayout.LayoutParams.WrapContent);
			param.SetMargins(5, 5, 5, 5);

			btnCreate = new Button (this);
			btnJoin = new Button (this);
			btnHangup = new Button (this);

			btnCreate.Click += delegate
			{
				showCreateRoomUI();
			};

			btnJoin.Click += delegate
			{
				showGetRoomUI();
			};

			btnHangup.Click += delegate
			{
				if (appRtcClient != null) {
					appRtcClient.LeaveRoom ();
					appRtcClient.Disconnect();
				}
			};

			btnCreate.SetText("Create", Button.BufferType.Normal);
			btnJoin.SetText("Join", Button.BufferType.Normal);
			btnHangup.SetText("HangUp", Button.BufferType.Normal);

			btnCreate.SetWidth((displaySize.X - 60) / 3);
			btnJoin.SetWidth((displaySize.X - 60) / 3);
			btnHangup.SetWidth((displaySize.X - 60) / 3);

			linerLayoutH.AddView (btnCreate, param);
			linerLayoutH.AddView (btnJoin, param);
			linerLayoutH.AddView (btnHangup, param);
			//

			// VideoStreamView layout
			LinearLayout linerLayoutV = new LinearLayout(this);
			linerLayoutV.Orientation = Android.Widget.Orientation.Vertical;

			vsv = new VideoStreamsView(this, new Point(displaySize.X-10, displaySize.Y-10-btnCreate.Height));

			linerLayoutV.AddView (linerLayoutH, param);
			linerLayoutV.AddView (vsv, param);
			//

			relativeLayout.AddView (linerLayoutV);

			SetContentView (relativeLayout, rlp);
        }

		private void showCreateRoomUI()
		{
			EditText roomInput = new EditText(this);
			roomInput.Text = "http://meeting.appinux.com";
			roomInput.SetSelection(roomInput.Text.Length);
			IDialogInterfaceOnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this, roomInput);
			AlertDialog.Builder builder = new AlertDialog.Builder(this);
			builder.SetMessage("Create room").SetView(roomInput).SetPositiveButton("Create", listener).Show();
		}

        private void showGetRoomUI()
        {
            EditText roomInput = new EditText(this);
            roomInput.Text = "http://meeting.appinux.com/?join=";
            roomInput.SetSelection(roomInput.Text.Length);
            IDialogInterfaceOnClickListener listener = new OnClickListenerAnonymousInnerClassHelper(this, roomInput);
            AlertDialog.Builder builder = new AlertDialog.Builder(this);
			builder.SetMessage("Enter room URL").SetView(roomInput).SetPositiveButton("Join", listener).Show();
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
			logAndToast ("Connecting to room...");
            appRtcClient.connectToRoom(roomUrl);
        }

		protected override void OnPause()
		{
			base.OnPause();
			if (vsv != null) {
				vsv.OnPause ();
			}

			if (appRtcClient.VideoSource != null)
			{
				appRtcClient.VideoSource.Stop ();
			}
		}

		protected override void OnResume()
		{
			base.OnResume();
			if (vsv != null) {
				vsv.OnResume ();
			}

			if (appRtcClient.VideoSource != null)
			{
				appRtcClient.VideoSource.Restart ();
			}
		}

		protected override void OnDestroy()
		{
			if (vsv != null) {
				vsv.OnPause ();
				vsv.Dispose ();
				vsv = null;
			}
			base.OnDestroy();
		}

		VideoTrack g_VideoTrack;
		public void onAddRenderer(Java.Lang.Object stream/*VideoTrack videoTrack*/, bool local)
		{
			if (local) {
				g_VideoTrack = (VideoTrack)stream;
				g_VideoTrack.AddRenderer (new VideoRenderer (new VideoCallbacks (this, vsv, VideoStreamsView.Endpoint.LOCAL)));
			} else {
				//RunOnUiThread (()=>videoTrack.AddRenderer (new VideoRenderer (new VideoCallbacks (this, vsv, VideoStreamsView.Endpoint.REMOTE))));
				RunOnUiThread (()=>g_VideoTrack.AddRenderer (new VideoRenderer (new VideoCallbacks (this, vsv, VideoStreamsView.Endpoint.REMOTE))));
			}
		}

		public void onStatusMessage(string msg)
		{
			logAndToast(msg);
		}

		public void onClose()
		{
			if (vsv != null) {
				//vsv.OnPause ();
			}
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

        private static void abortUnless(bool condition, string msg)
        {
            if (!condition)
            {
                throw new Exception(msg);
            }
        }

        private void logAndToast(string msg)
        {
			RunOnUiThread(() => Toast.MakeText (this, msg, ToastLength.Long).Show ());
        }
    }
}