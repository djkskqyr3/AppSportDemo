using System;
using System.Text;
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

		private readonly UserMediaObserver userMediaObserver;
		private readonly AddStreamObserver addStreamObserver;

		private PeerConnectionFactory factory;
        private SDPObserver sdpObserver;

		private string app_id;
		private string userId;
		private string joinRoom;
		private bool detectedRoom;
		private bool is_setup = true;

		private MediaStream stream;       
		private JSONObject room;
        

        private MediaConstraints offerAnswerConstraints;
		private MediaConstraints videoConstrants;
		private MediaConstraints optionalArgument;

		private PeerConnection peer = null;

		private string domain = "http://meeting.appinux.com";
 
		private List<PeerConnection.IceServer> iceServers = new List<PeerConnection.IceServer>();        

		public void onMeeting(JSONObject value)
        {
			string roomid = (string)value.Get("roomid");
			if (joinRoom != null && roomid != joinRoom)
                return;

            if (detectedRoom) return;
            detectedRoom = true;

			this.Meet(value);
        }

        public void initSignaler()
        {
            signaler = new Signaler(this);
        }

		public interface UserMediaObserver
		{
			void onUserMediaObserver (JSONObject constraints);
		}

		void captureUserMedia()
		{
			if (capturingUserMedia) return;
			capturingUserMedia = true;

			JSONObject json = new JSONObject();
			jsonPut(json, "audio", true);
			jsonPut(json, "video", true);

			userMediaObserver.onUserMediaObserver (json);
		}

		public void onStream(PeerConnectionFactory factory, MediaStream stream)
		{
			this.stream = stream;
			this.factory = factory;

			if (is_setup == true)
			{
				Log.Debug(TAG, Status.Status01);

				JSONObject json = new JSONObject();
				jsonPut(json, "roomid", joinRoom);
				jsonPut(json, "user_hash", this.userId);
				jsonPut(json, "url", app_id==null?"":app_id);

				signaler.broadcast(json);
			}
			else
			{
				Log.Debug(TAG, Status.Status02);

				JSONObject json = new JSONObject();
				jsonPut(json, "to", (string)this.room.Get("userid"));
				jsonPut(json, "roomid", this.joinRoom);
				jsonPut(json, "url", this.app_id==null?"":this.app_id);
				jsonPut(json, "user_hash", signaler.userid);

				signaler.Join(json);
			}

			capturingUserMedia = false;
		}

		void onError()
		{
			JSONObject json = new JSONObject();
			jsonPut(json, "leaving", true);
			jsonPut(json, "roomid", joinRoom);
			jsonPut(json, "app_id", app_id==null?"":app_id);
			jsonPut(json, "user_hash", this.userId);

			signaler.signal(json);
		}

		// setup new meeting room
		public void Setup(string join, string userid, string app_id)
		{
			is_setup = true;
			this.joinRoom = join;
			this.userId = userid;
			this.app_id = app_id;

			Log.Debug(TAG, Status.Status00);

			if (signaler == null)
			{
				initSignaler();
			}

			captureUserMedia();
		}

		public void Meet(System.Object room)
		{
			is_setup = false;            

			Log.Debug(TAG, Status.Status00);

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

            public Signaler(NodeClient outerInstance)
            {
                this.outerInstance = outerInstance;
				if (this.userid == null)
                {
					this.userid = outerInstance.getToken();
                }
                else
                {
					this.userid = outerInstance.userId;
                }

				_socket = new Client(string.Format(outerInstance.domain));

				_socket.Opened += SocketOpened;
				_socket.Message += SocketMessage;
				_socket.SocketConnectionClosed += SocketConnectionClosed;
				_socket.Error += SocketError;

				// make the socket.io connection
				_socket.Connect();
            }

			void SocketOpened(object sender, System.EventArgs e)
			{

			}

			void SocketConnectionClosed(object sender, System.EventArgs e)
			{
				Log.Debug(TAG, "WebSocketConnection was terminated!");
			}

			void SocketError(object sender, ErrorEventArgs e)
			{
				Log.Debug(TAG, "socket client error:");
				Log.Debug(TAG, e.Message); 
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

				if (!isbroadcaster && value.Has ("roomid") && value.Has ("broadcasting") && !sentParticipationRequest) {
					this.outerInstance.onMeeting (value);
					Log.Debug (TAG, Status.Status03);
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
					string userId = (string)value.Get ("userid");
					if (to.Equals (this.userid)) {
						if (!roomFull) {
							roomFull = true;
							participationRequest(userId);
							Log.Debug(TAG, Status.Status04);
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
						Log.Debug (TAG, Status.Status09);
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
                    outerInstance.peer.AddIceCandidate((IceCandidate)message.Get("candidate"));
                }
            }

            public class Options
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
                    
                    outerInstance.addStreamObserver.onAddStreamObserver(stream);
                }
            }

            // call only for session initiator
            public void broadcast(JSONObject json)
            {
                if (json.Has("roomid"))
                    this.roomid = (string)json.Get("roomid");
                else
					this.roomid = outerInstance.getToken();

                this.isbroadcaster = true;

                //(function transmit() {}

                JSONObject broadcastJson = new JSONObject();
                jsonPut(broadcastJson, "roomid", this.roomid);
                jsonPut(broadcastJson, "url", (string)json.Get("url"));
                jsonPut(broadcastJson, "user_hash", (string)json.Get("user_hash"));
                jsonPut(broadcastJson, "broadcasting", true);

                signal(broadcastJson);
                //

                // if broadcaster leaves; clear all JSON files from Firebase servers
                //if (socket.onDisconnect) socket.onDisconnect().remove();
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

            void leaveRoom()
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
                jsonPut(json, "userid", userid);

				SocketIOClient.Messages.IMessage msg = new SocketIOClient.Messages.JSONMessage(json.ToString());
				_socket.Send (msg);
            }

            // custom signaling implementations
            /*SocketIOClient.Client socket = outerInstance.openSignalingChannel(function(message) {
                message = JSON.parse(message);
                if (message.userid != userid) {
                    if (message.leaving) {
					    if(message.roomid == signaler.roomid) {
						    signaler.roomFull = false;

						    root.onuserleft(message.userid);
						    var peer = peers[message.userid];
						    if (peer && peer.peer) {
							    try {
								    peer.peer.close();
							    } catch(e) {
							    }
							    delete peers[message.userid];
						    }
					    }
                    } else signaler.onmessage(message);
                }
            });*/
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

                if (newState != null/* && newState == IceGatheringState.COMPLETE*/)
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

                if (candidate != null)
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

				if (outerInstance.stream != null)
					outerInstance.peer.AddStream(outerInstance.stream, new MediaConstraints());

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

                //outerInstance.activity.RunOnUiThread(() => stream.VideoTracks.Get(0).Dispose());
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

		public interface AddStreamObserver
		{
			void onAddStreamObserver (MediaStream stream);
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

                Log.Debug(TAG, "Sending " + origSdp.Type);

                SessionDescription sdp = new SessionDescription(origSdp.Type, outerInstance.preferISAC(origSdp.Description));
                outerInstance.peer.SetLocalDescription(outerInstance.sdpObserver, sdp);
                option.onSdp(sdp, option.to);
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

		public NodeClient(Activity activity, UserMediaObserver userMediaObserver, AddStreamObserver addStreamObserver)
		{
            this.activity = activity;
			this.userMediaObserver = userMediaObserver;
			this.addStreamObserver = addStreamObserver;

            sdpObserver = new SDPObserver(this);

            //Logging.EnableTracing("", EnumSet.Of(0), (Logging.Severity)4);

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
				this.app_id = getToken ();

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

        public void disconnect()
        {
            if (signaler != null)
            {
				signaler._socket.Close ();
				signaler._socket = null;
				signaler = null;
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

		public MediaConstraints OptionalArgument
		{
			get
			{
				return optionalArgument;
			}
		}

		public MediaConstraints VideoConstrants
		{
			get
			{
				return videoConstrants;
			}
		}
    }
}