using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;
using Java.IO;
using Java.Lang;
using Java.Net;
using Java.Util;
using Java.Util.Regex;
using Org.Json;
using Org.Webrtc;

using Pattern = Android.OS.Pattern;
using Exception = System.Exception;
using IceGatheringState = Org.Webrtc.PeerConnection.IceGatheringState;

using SocketIOClient;

namespace Appspotdemo.Mono.Droid
{
    public class NodeClient
    {
        private const string TAG = "NodeClient";

        private readonly Activity activity;

		private Signaler signaler;
		private bool capturingUserMedia = false;

		private NodeClientObserver nodeClientObserver;

		private PeerConnectionFactory factory;
        private SDPObserver sdpObserver;

		private string app_id;
		private string userId;
		private string joinRoom;
		private bool detectedRoom;
		private bool is_setup = true;

		private MediaStream stream;       
		private JSONObject room;
		private Org.Webrtc.VideoSource videoSource;

        private MediaConstraints offerAnswerConstraints;
		private MediaConstraints videoConstrants;
		private MediaConstraints optionalArgument;

		private PeerConnection peer = null;

		private string domain = "http://meeting.appinux.com";
 
		private List<PeerConnection.IceServer> iceServers = new List<PeerConnection.IceServer>();        

		public interface NodeClientObserver
		{
			//void onAddRenderer (VideoTrack videoTrack, bool local);
			void onAddRenderer (Java.Lang.Object stream, bool local);
			void onStatusMessage(string msg);
			void onClose();
		}

		public void onMeeting(JSONObject value)
        {
			string roomid = (string)value.Get("roomid");
			if (joinRoom != null && roomid != joinRoom) {
				if (nodeClientObserver != null)
					nodeClientObserver.onStatusMessage("Please input correct room number.");
				LeaveRoom ();
				return;
			}

            if (detectedRoom) return;
            detectedRoom = true;

			this.Meet(value);
        }

        public void initSignaler()
        {
            signaler = new Signaler(this);
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


		void captureUserMedia()
		{
			if (capturingUserMedia) return;
			capturingUserMedia = true;

			JSONObject json = new JSONObject();
			jsonPut(json, "audio", true);
			jsonPut(json, "video", true);

			factory = new PeerConnectionFactory();

			if (nodeClientObserver != null)
				nodeClientObserver.onStatusMessage("Creating local video source...");

			this.stream = factory.CreateLocalMediaStream("ARDAMS");
			if (json.Has ("video")) {
				VideoCapturer capturer = VideoCapturer;

				if (capturer == null) {
					onError ();
					return;
				}
				videoSource = factory.CreateVideoSource (capturer, videoConstrants);
				VideoTrack videoTrack = factory.CreateVideoTrack("ARDAMSv0", videoSource);

				if (nodeClientObserver != null)
					nodeClientObserver.onAddRenderer (videoTrack, true);

				this.stream.AddTrack(videoTrack);
			}

			if (json.Has ("audio")) {

				this.stream.AddTrack(factory.CreateAudioTrack("ARDAMSa0"));
			}


			if (nodeClientObserver != null)
				nodeClientObserver.onStatusMessage("Waiting for ICE candidates...");

			if (is_setup == true)
			{
				Log.Debug(TAG, Status.Status01);

				JSONObject broad = new JSONObject();
				jsonPut(broad, "roomid", joinRoom);
				jsonPut(broad, "user_hash", this.userId);
				jsonPut(broad, "url", app_id);

				signaler.broadcast(broad);
			}
			else
			{
				Log.Debug(TAG, Status.Status02);

				JSONObject broad = new JSONObject();
				jsonPut(broad, "to", (string)this.room.Get("userid"));
				jsonPut(broad, "roomid", this.joinRoom);
				jsonPut(broad, "url", this.app_id==null?"":this.app_id);
				jsonPut(broad, "user_hash", signaler.userid);

				signaler.Join(broad);
			}

			capturingUserMedia = false;
		}

		public void onError()
		{
			signaler.LeaveRoom ();
		}

		// setup new meeting room
		public void Setup(string join, string userid, string app_id)
		{
			is_setup = true;
			this.joinRoom = join;
			this.userId = userid;
			this.app_id = app_id;

			if (nodeClientObserver != null)
				nodeClientObserver.onStatusMessage("Create room number(" + this.joinRoom + ")");

			if (signaler == null)
			{
				initSignaler();
			}

			captureUserMedia();
		}

		public void Meet(System.Object room)
		{
			is_setup = false;            

			if (nodeClientObserver != null)
				nodeClientObserver.onStatusMessage(Status.Status00);

			if (signaler == null)
			{
				initSignaler();
			}

			string type = "";

			if (room.GetType() == type.GetType()){

				this.joinRoom = (string)room;
			}
			else
			{
				this.room = (JSONObject)room;
				captureUserMedia();
			}
		}

        public class Signaler
        {
			private readonly NodeClient outerInstance;

			public string userid;
			private bool isbroadcaster = false;
			private bool sentParticipationRequest = false;

			// object to store ICE candidates for answerer
			private List<IceCandidate> candidates = new List<IceCandidate>();

			// object to store all connected participants's ids
			private Dictionary<string, string> participants = new Dictionary<string, string>();

            private bool roomFull = false;
            private bool creatingOffer = false;
            
            private string roomid;

			public Client _socket;       

			private System.Timers.Timer mTimer;

            public Signaler(NodeClient outerInstance)
            {
				this.outerInstance = outerInstance;
				if (outerInstance.userId == null)
                {
					outerInstance.userId = outerInstance.getToken();
                }

				this.userid = outerInstance.userId;               

				_socket = new Client(string.Format(outerInstance.domain));

				_socket.Opened += SocketOpened;
				_socket.Message += SocketMessage;
				_socket.SocketConnectionClosed += SocketConnectionClosed;
				_socket.Error += SocketError;

				// make the socket.io connection
				_socket.Connect();
            }

			public void Dispose()
			{
				_socket.Close ();
				_socket = null;
			}

			void SocketOpened(object sender, System.EventArgs e)
			{

			}

			void SocketConnectionClosed(object sender, System.EventArgs e)
			{
				if (outerInstance.nodeClientObserver != null)
					outerInstance.nodeClientObserver.onStatusMessage("WebSocketConnection was terminated!");
			}

			void SocketError(object sender, ErrorEventArgs e)
			{
				if (outerInstance.nodeClientObserver != null)
					outerInstance.nodeClientObserver.onStatusMessage("socket client error:" + e.Message);
			}

			void SocketMessage(object sender, MessageEventArgs e)
			{
				// uncomment to show any non-registered messages
				if (string.IsNullOrEmpty (e.Message.Event)) {
					Log.Debug (TAG, "Generic SocketMessage: {0}", e.Message.MessageText);
					return;
				}

				JSONObject json = new JSONObject(e.Message.Json.ToJsonString());
				string name = e.Message.Json.Name;
				dynamic[] args = e.Message.Json.Args;

				if (name == null || args == null)
					return;
                    
				JSONObject value = new JSONObject(json.GetJSONArray ("args").GetString(0));

				string userId = (string)value.Get ("userid");
				if (userId.Equals (this.userid))
					return;
	
				//if conference terminate
				if (value.Has ("leaving")) {

					bool leaving = value.GetBoolean ("leaving");
					if (leaving == true) {
						if (outerInstance.joinRoom.Equals ((string)value.Get ("roomid"))) {

							this.roomFull = false;

							if (outerInstance.peer != null) {
								try
								{
									outerInstance.LeaveRoom();

									outerInstance.peer.Close();
									outerInstance.peer.Dispose();
									outerInstance.peer = null;

									if(outerInstance.userId != null && userId != outerInstance.userId){
										if (outerInstance.nodeClientObserver != null)
											outerInstance.nodeClientObserver.onStatusMessage(Status.Status10);
									}
									else {
										if (outerInstance.nodeClientObserver != null)
											outerInstance.nodeClientObserver.onStatusMessage(Status.Status05);
									}
								}
								catch (Exception ex)
								{
									throw new Exception("Error", ex);
								}
							}

							outerInstance.nodeClientObserver.onClose ();

							System.Threading.Thread.Sleep(5000);

							if (outerInstance.videoSource != null)
							{
								outerInstance.videoSource.Stop ();
								outerInstance.videoSource.Dispose ();
								outerInstance.videoSource = null;
							}

							return;
						}
					}
				}

				if (!isbroadcaster && value.Has ("roomid") && value.Has ("broadcasting") && !sentParticipationRequest) {
					this.outerInstance.onMeeting (value);
				} else {
					Log.Debug (TAG, e.Message.Json.ToJsonString());
				}

				//if someone shared SDP
				if (value.Has ("sdp")) {
					string to = (string)value.Get ("to");
					if (to.Equals (userid)) {
						this.onSdp(value);
					}
				}

				// if someone shared ICE
				if (value.Has("candidate")) {	
					string to = (string)value.Get ("to");
					if (to.Equals (userid)) {
						this.onIce(value);
					}
				}

				// if someone sent participation request
				if (value.Has("participationRequest")) {	
					string to = (string)value.Get ("to");
					if (to.Equals (this.userid)) {
						if (!roomFull) {
							roomFull = true;
							participationRequest(userId);
							if (outerInstance.nodeClientObserver != null)
								outerInstance.nodeClientObserver.onStatusMessage(Status.Status04);
						}
						else 
						{
							JSONObject hangedup = new JSONObject();
							jsonPut(hangedup, "isRoomFull", true);
							jsonPut(hangedup, "messageFor", userId);

							signal(json);
						}
					}
				}

				// session initiator transmitted new participant's details
				// it is useful for multi-user connectivity

				string newcomer = "";
				string partcipant = null;
				if (value.Has ("newcomer")) {
					newcomer = (string)value.Get ("newcomer");
					partcipant = participants[newcomer];
				}

				if (!isbroadcaster && value.Has("conferencing") && 
					newcomer != this.userid && partcipant == null) {

					participants[newcomer] = newcomer;

					if (outerInstance.stream != null) {
						JSONObject part = new JSONObject();
						jsonPut(part, "participationRequest", true);
						jsonPut(part, "to", newcomer);

						signal(part);
					}
				}

				if (value.Has("isRoomFull")) {		
					string messageFor = (string)value.Get ("messageFor");
					if (messageFor.Equals (this.userid)) {
						if (outerInstance.nodeClientObserver != null)
							outerInstance.nodeClientObserver.onStatusMessage(Status.Status09);
					}
				}
			}

			void participationRequest(string _userid)
            {
                // it is appeared that 10 or more users can send 
                // participation requests concurrently
                // onicecandidate fails in such case
                if (!creatingOffer)
                {
                    creatingOffer = true;
                    
					createOffer(_userid);

					creatingOffer = false;

					if (participants != null && (participants.Count > 0))
                    {
                        repeatedlyCreateOffer();
                    }
                }
                else
                {
					participants[_userid] = _userid;
                }
            }

            // reusable function to create new offer
            void createOffer(string to)
            {
                Options _options = new Options(outerInstance, this);

				_options.stream = outerInstance.stream;
				_options.to = to;
				_options.type = "offer";

                Offer offer = new Offer(outerInstance);
                offer.createOffer(_options);
            }
 
            // reusable function to create new offer repeatedly
            void repeatedlyCreateOffer()
            {
				Log.Debug(TAG, participants.ToString());
            }

             // if someone shared SDP
			void onSdp(JSONObject message)
            {
				JSONObject sdp = message.GetJSONObject ("sdp");
				if (sdp.Has("type") && sdp.Get("type").Equals("offer"))
                {
					SessionDescription _sdp = new SessionDescription (SessionDescription.SessionDescriptionType.FromCanonicalForm("offer"), outerInstance.preferISAC((string)sdp.Get("sdp")));
					Options _options = new Options (outerInstance, this);
					_options.stream = outerInstance.stream;
					_options.sdp = _sdp;
					_options.to = (string)message.Get ("userid");
					_options.type = "answer";

                    Answer answer = new Answer(outerInstance);
                    answer.createAnswer(_options);
                }

				if (sdp.Has("type") && sdp.Get("type").Equals("answer"))
	            {
					SessionDescription _sdp = new SessionDescription (SessionDescription.SessionDescriptionType.FromCanonicalForm("answer"), outerInstance.preferISAC((string)sdp.Get("sdp")));
                    outerInstance.peer.SetRemoteDescription(outerInstance.sdpObserver, _sdp);
                }
            }

            // if someone shared ICE
			void onIce(JSONObject message)
            {
                if (message.Has("candidate"))
                {
					JSONObject json = new JSONObject((string)message.Get("candidate"));
					outerInstance.peer.AddIceCandidate (new IceCandidate(json.GetString("sdpMid"), json.GetInt("sdpMLineIndex"), json.GetString("candidate")));
                }
            }

			public class Options : Java.Lang.Object
            {
                private readonly NodeClient outerInstance;
                private readonly Signaler signaler;
                public MediaStream stream;
                public SessionDescription sdp;
                public string to;
				public string type;

                public Options(NodeClient outerInstance, Signaler signaler)
                {
                    this.outerInstance = outerInstance;
                    this.signaler = signaler;
                }

                public void onSdp(SessionDescription sdp, string to)
                {
					JSONObject jsonSDP = new JSONObject();
					jsonPut(jsonSDP, "type", type);
					jsonPut(jsonSDP, "sdp", sdp.Description);

                    JSONObject json = new JSONObject();
					jsonPut(json, "sdp", jsonSDP);
                    jsonPut(json, "to", to);

                    signaler.signal(json);
                }

                public void onIcecandidate(IceCandidate candidate, string to)
                {
                    JSONObject candi = new JSONObject();
					jsonPut(candi, "sdpMLineIndex", candidate.SdpMLineIndex);
					jsonPut(candi, "sdpMid", candidate.SdpMid);
					jsonPut(candi, "candidate", candidate.Sdp.ToString());

                    JSONObject json = new JSONObject();
                    jsonPut(json, "candidate", candi);
                    jsonPut(json, "to", to);

                    signaler.signal(json);
                }

				public void onAddstream(MediaStream stream, string to)
                {
					Log.Debug(TAG, "Options::onAddstream >>> " + stream);
                    
					try
					{
						abortUnless(stream.AudioTracks.Size() <= 1 && stream.VideoTracks.Size() <= 1, "Weird-looking stream: " + stream);
						if (stream.VideoTracks.Size() == 1) {
							//Org.Webrtc.VideoTrack track = (Org.Webrtc.VideoTrack)stream.VideoTracks.Get(0);
							outerInstance.nodeClientObserver.onAddRenderer(stream.VideoTracks.Get(0), false);
						}
					}
					catch(Exception e) {
						if (outerInstance.nodeClientObserver != null)
							outerInstance.nodeClientObserver.onStatusMessage(e.Message);
					}
                }
            }

			private JSONObject sendBroadcast;

            // call only for session initiator
            public void broadcast(JSONObject json)
            {
                if (json.Has("roomid"))
                    this.roomid = (string)json.Get("roomid");
                else
					this.roomid = outerInstance.getToken();

                this.isbroadcaster = true;
				this.sendBroadcast = json;

				mTimer = new System.Timers.Timer();
				mTimer.Interval=3000;
				mTimer.Elapsed+=new System.Timers.ElapsedEventHandler(OnTimedEvent);
				mTimer.Enabled=true;
            }

			private void OnTimedEvent(object source, System.Timers.ElapsedEventArgs e)
			{
				JSONObject broadcastJson = new JSONObject();
				jsonPut(broadcastJson, "roomid", this.roomid);
				jsonPut(broadcastJson, "url", (string)this.sendBroadcast.Get("url"));
				jsonPut(broadcastJson, "user_hash", (string)this.sendBroadcast.Get("user_hash"));
				jsonPut(broadcastJson, "broadcasting", true);

				signal(broadcastJson);
			}

            // called for each new participant
			public void Join(JSONObject config)
            {
				this.roomid = (string)config.Get("roomid");

                JSONObject json = new JSONObject();
                jsonPut(json, "participationRequest", true);
				jsonPut(json, "to", (string)config.Get("to"));
				jsonPut(json, "url", (string)config.Get("url"));
				jsonPut(json, "user_hash", (string)config.Get("user_hash"));
				jsonPut(json, "roomid", (string)config.Get("roomid"));

                signal(json);

                sentParticipationRequest = true;
            }

			public void LeaveRoom()
            {
                JSONObject json = new JSONObject();
                jsonPut(json, "leaving", true);
				jsonPut(json, "roomid", outerInstance.joinRoom);
                jsonPut(json, "app_id", outerInstance.app_id);
                jsonPut(json, "user_hash", outerInstance.userId);

                signal(json);
            }

            // method to signal the data
            public void signal(JSONObject json)
            {
				jsonPut(json, "userid", outerInstance.userId);

				SocketIOClient.Messages.IMessage msg = new SocketIOClient.Messages.JSONMessage(json.ToString());
				_socket.Send (msg);
            }
        }

        // Implementation detail: observe ICE & stream changes and react accordingly.
        
        public class Offer : Java.Lang.Object, PeerConnection.IObserver
        {
            private readonly NodeClient outerInstance;
            private Signaler.Options option;

            public Offer(NodeClient outerInstance)
			{
				this.outerInstance = outerInstance;
			}

            public void createOffer(Signaler.Options _option)
            {
                this.option = _option;
                outerInstance.sdpObserver.option = _option;

                outerInstance.peer = outerInstance.factory.CreatePeerConnection(outerInstance.iceServers, outerInstance.optionalArgument, this);

                if (_option.stream != null)
                    outerInstance.peer.AddStream(_option.stream, new MediaConstraints());

                outerInstance.peer.CreateOffer(outerInstance.sdpObserver, outerInstance.offerAnswerConstraints);
            }

            public void OnIceGatheringChange(PeerConnection.IceGatheringState newState)
            {
                Log.Debug(TAG, "Offer::OnIceGatheringChange == >" + newState);

				if (newState != null && newState == IceGatheringState.Complete)
                {
                    returnSDP();
                }
            }

            private void returnSDP()
            {
                Log.Debug(TAG, "sharing localDescription" + outerInstance.peer.LocalDescription);
                this.option.onSdp(outerInstance.peer.LocalDescription, this.option.to);
            }

            public void OnAddStream(MediaStream stream)
            {
                Log.Debug(TAG, "Offer::OnAddStream == >" + stream);

                if (stream != null)
                    this.option.onAddstream(stream, this.option.to);
            }

            public void OnIceCandidate(IceCandidate candidate)
            {
                Log.Debug(TAG, "Offer::OnIceCandidate == >" + candidate);

				if (candidate == null)
                    returnSDP();
                else
                    Log.Debug(TAG, "injecting ice in sdp: " + candidate);              
            }

            public void OnSignalingChange(PeerConnection.SignalingState newState)
            {
                Log.Debug(TAG, "Offer::OnSignalingChange == >" + newState);
            }

            public void OnIceConnectionChange(PeerConnection.IceConnectionState newState)
            {
                Log.Debug(TAG, "Offer::OnIceConnectionChange == >" + newState);
            }

            public void addIceCandidate(IceCandidate candidate)
            {
                outerInstance.peer.AddIceCandidate(candidate);
            }

            public void OnRemoveStream(MediaStream stream)
            {
                Log.Debug(TAG, "Offer::OnRemoveStream == >");

                //outerInstance.activity.RunOnUiThread(() => stream.VideoTracks.Get(0).Dispose());
            }

			public void OnRenegotiationNeeded ()
			{

			}

            public void OnDataChannel(DataChannel dc)
            {
                Log.Debug(TAG, "Offer::OnDataChannel == >");
            }

            public void OnError()
            {
                Log.Debug(TAG, "Offer::OnError == >");
            }
        }

        // Implementation detail: observe ICE & stream changes and react accordingly.
        public class Answer : Java.Lang.Object, PeerConnection.IObserver
		{
			private readonly NodeClient outerInstance;
            private Signaler.Options option;
            
			public Answer(NodeClient outerInstance)
			{
				this.outerInstance = outerInstance;
			}

			public void createAnswer(Signaler.Options _option)
			{
                this.option = _option;
                outerInstance.sdpObserver.option = _option;

                outerInstance.peer = outerInstance.factory.CreatePeerConnection(outerInstance.iceServers, outerInstance.optionalArgument, this);

				if (_option.stream != null)
					outerInstance.peer.AddStream(_option.stream, new MediaConstraints());

				outerInstance.peer.SetRemoteDescription(outerInstance.sdpObserver, _option.sdp);
                 
				outerInstance.peer.CreateAnswer(outerInstance.sdpObserver, outerInstance.offerAnswerConstraints);
			}

            public void OnAddStream(MediaStream stream)
            {
                Log.Debug(TAG, "Answer::OnAddStream == >" + stream);

                if (stream != null)
                    this.option.onAddstream(stream, this.option.to);
            }

            public void OnIceCandidate(IceCandidate candidate)
            {
                if (candidate != null)
                    this.option.onIcecandidate(candidate, this.option.to);
            }

            public void OnSignalingChange(PeerConnection.SignalingState newState)
            {
                Log.Debug(TAG, "Answer::OnSignalingChange == >" + newState);
				if (newState == PeerConnection.SignalingState.Closed) {
				}
            }

            public void OnIceConnectionChange(PeerConnection.IceConnectionState newState)
            {
                Log.Debug(TAG, "Answer::OnIceConnectionChange == >" + newState);
            }

            public void addIceCandidate(IceCandidate candidate)
            {
                outerInstance.peer.AddIceCandidate(candidate);
            }
            
            public void OnIceGatheringChange(PeerConnection.IceGatheringState newState)
            {
                Log.Debug(TAG, "Answer::OnIceGatheringChange == >" + newState);
            }

            public void OnRemoveStream(MediaStream stream)
            {
                Log.Debug(TAG, "Answer::OnRemoveStream == >");

                outerInstance.activity.RunOnUiThread(() => stream.VideoTracks.Get(0).Dispose());
            }

			public void OnRenegotiationNeeded ()
			{

			}

            public void OnDataChannel(DataChannel dc)
            {
                Log.Debug(TAG, "Answer::OnDataChannel == >");
            }

            public void OnError()
            {
                Log.Debug(TAG, "Answer::OnError == >");
            }
		}

        private class SDPObserver : Java.Lang.Object, ISdpObserver
        {
            private readonly NodeClient outerInstance;
            public Signaler.Options option;

            public SDPObserver(NodeClient outerInstance)
            {
                this.outerInstance = outerInstance;
            }

            public void OnCreateSuccess(SessionDescription origSdp)
            {
                Log.Debug(TAG, "SDPObserver::OnCreateSuccess == >");

				//SessionDescription sdp = new SessionDescription(origSdp.Type, outerInstance.preferISAC(origSdp.Description));
				outerInstance.peer.SetLocalDescription(outerInstance.sdpObserver, origSdp);

				if (origSdp.Type == SessionDescription.SessionDescriptionType.Answer) {
					option.onSdp(origSdp, option.to);
				}
            }
			 
            public void OnSetSuccess()
            {
                Log.Debug(TAG, "SDPObserver::OnSetSuccess == >");
            }

            public void OnCreateFailure(string error)
            {
                Log.Debug(TAG, "SDPObserver::OnCreateFailure == >");
            }

            public void OnSetFailure(string error)
            {
                Log.Debug(TAG, "SDPObserver::OnSetFailure == >");
            }
        }

		public NodeClient(Activity activity, NodeClientObserver nodeClientObserver)
		{
            this.activity = activity;
			this.nodeClientObserver = nodeClientObserver;

            sdpObserver = new SDPObserver(this);

			//Logging.EnableTracing("logcat:", EnumSet.Of(Logging.TraceLevel.TraceAll), Logging.Severity.LsError);

            this.iceServers.Add(new PeerConnection.IceServer("turn:turn.appinux.com", "htk", "12345678@X"));
            this.iceServers.Add(new PeerConnection.IceServer("stun:turn.appinux.com"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.l.google.com:19302"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.sipgate.net"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:217.10.68.152"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.sipgate.net:10000"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:217.10.68.152:10000"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:23.21.150.121:3478"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:216.93.246.18:3478"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:66.228.45.110:3478"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:173.194.78.127:19302"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:74.125.142.127:19302"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:provserver.televolution.net"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:sip1.lakedestiny.cordiaip.com"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun1.voiceeclipse.net"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun01.sipphone.com"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.callwithus.com"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.counterpath.net"));
			this.iceServers.Add(new PeerConnection.IceServer("stun:stun.endigovoip.com"));

            offerAnswerConstraints = new MediaConstraints();
			offerAnswerConstraints.Mandatory.Add(new MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"));
			offerAnswerConstraints.Mandatory.Add(new MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"));

			optionalArgument = new MediaConstraints ();
			optionalArgument.Optional.Add(new MediaConstraints.KeyValuePair("DtlsSrtpKeyAgreement", "true"));

			videoConstrants = new MediaConstraints ();
		}

        public void connectToRoom(string url)
        {			
			if (url.IndexOf ('?') < 0) {

                this.domain = url;

				this.userId = getToken ();
				this.joinRoom = getToken ();
				this.app_id = "";

				Setup (joinRoom, userId, app_id);
			} else {

				string param = url.Substring(url.IndexOf('?')+1);
				string[] arr = param.Split('&');
				for (int i = 0; i < arr.Length; i++) {
					string[] arr1 = arr[i].Split('=');
					if (arr1 [0].Equals ("join")) {
						Meet (arr1 [1]);
						break;
					}
				}
			}
        }

		public void LeaveRoom()
		{
			if (signaler != null) {
				signaler.LeaveRoom ();
				signaler.Dispose ();
				signaler = null;
			}

			detectedRoom = false;
		}

		public void Disconnect()
        {
			if (peer != null) {
				peer.Close ();
				peer = null;
			}
        }

		private static void abortUnless(bool condition, string msg)
		{
			if (!condition)
			{
				throw new Exception(msg);
			}
		}

		// Mangle SDP to prefer ISAC/16000 over any other audio codec.
		private string preferISAC(string sdpDescription)
		{
			string[] lines = sdpDescription.Split("\n", true);
			int mLineIndex = -1;
			string isac16kRtpMap = null;
			Java.Util.Regex.Pattern isac16kPattern = Java.Util.Regex.Pattern.Compile("^a=rtpmap:(\\d+) ISAC/16000[\r]?$");
			for (int i = 0; (i < lines.Length) && (mLineIndex == -1 || isac16kRtpMap == null); ++i)
			{
				if (lines[i].StartsWith("m=audio "))
				{
					mLineIndex = i;
					continue;
				}
				Matcher isac16kMatcher = isac16kPattern.Matcher(lines[i]);
				if (isac16kMatcher.Matches())
				{
					isac16kRtpMap = isac16kMatcher.Group(1);
					continue;
				}
			}
			if (mLineIndex == -1)
			{
				Log.Debug(TAG, "No m=audio line, so can't prefer iSAC");
				return sdpDescription;
			}
			if (isac16kRtpMap == null)
			{
				Log.Debug(TAG, "No ISAC/16000 line, so can't prefer iSAC");
				return sdpDescription;
			}
			string[] origMLineParts = lines[mLineIndex].Split(" ", true);
			System.Text.StringBuilder newMLine = new System.Text.StringBuilder();
			int origPartIndex = 0;
			// Format is: m=<media> <port> <proto> <fmt> ...
			newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
			newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
			newMLine.Append(origMLineParts[origPartIndex++]).Append(" ");
			newMLine.Append(isac16kRtpMap).Append(" ");
			for (; origPartIndex < origMLineParts.Length; ++origPartIndex)
			{
				if (!origMLineParts[origPartIndex].Equals(isac16kRtpMap))
				{
					newMLine.Append(origMLineParts[origPartIndex]).Append(" ");
				}
			}
			lines[mLineIndex] = newMLine.ToString();
			System.Text.StringBuilder newSdpDescription = new System.Text.StringBuilder();
			foreach (string line in lines)
			{
				newSdpDescription.Append(line).Append("\n");
			}
			return newSdpDescription.ToString();
		}

		private string getToken()
		{
			System.Random rnd = new System.Random();
			return rnd.Next(65536).ToString();
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

		public MediaConstraints VideoConstrants
		{
			get
			{
				return videoConstrants;
			}
		}

		public VideoSource VideoSource
		{
			get {
				return videoSource;
			}
		}
    }
}