using Newtonsoft.Json;

namespace Appspotdemo.Mono.Droid
{
	[JsonObject(MemberSerialization.OptIn)]
	public class MeetingRequest
	{
		[JsonProperty]
		public string Message { get; set; }

		[JsonProperty]
		public string To { get; set; }

		[JsonProperty]
		public string From { get; set; }

		[JsonProperty]
		public string Meeting_Id { get; set; }

		[JsonProperty]
		public bool First { get; set; }

		public string ToJsonString()
		{
			return JsonConvert.SerializeObject(this);
		}
		public static MeetingRequest Deserialize(string jsonString)
		{
			return JsonConvert.DeserializeObject<MeetingRequest>(jsonString);
		}
	}
	[JsonObject(MemberSerialization.OptIn)]
	public class MeetingStartedMessage
	{
		[JsonProperty]
		public string Meeting_Id { get; set; }

		[JsonProperty]
		public string[] Participants { get; set; }

		public string ToJsonString()
		{
			return JsonConvert.SerializeObject(this);
		}
		public static MeetingRequest Deserialize(string jsonString)
		{
			return JsonConvert.DeserializeObject<MeetingRequest>(jsonString);
		}
	}


	[JsonObject(MemberSerialization.OptIn)]
	public class SubscribeRequest
	{
		[JsonProperty]
		public string Username { get; set; }

		[JsonProperty]
		public bool IsAdmin { get; set; }

		public string ToJsonString()
		{
			return JsonConvert.SerializeObject(this);
		}
		public static SubscribeRequest Deserialize(string jsonString)
		{
			return JsonConvert.DeserializeObject<SubscribeRequest>(jsonString);
		}
	}

    [JsonObject(MemberSerialization.OptIn)]
    public class MeetingMessage
    {
        [JsonProperty]
        public string RoomId { get; set; }

        [JsonProperty]
        public bool Broadcasting { get; set; }
        
        [JsonProperty]
        public string Sdp { get; set; }
 
        [JsonProperty]
        public string To { get; set; }
        
        [JsonProperty]
        public string[] Candidate { get; set; }

        [JsonProperty]
        public bool ParticipationRequest { get; set; }
        
        [JsonProperty]
        public string UserId { get; set; }
        
        [JsonProperty]
        public bool Conferencing { get; set; }
        
        [JsonProperty]
        public string NewComer { get; set; }
        
        [JsonProperty]
        public bool IsRoomFull { get; set; }
        
        [JsonProperty]
        public string MessageFor { get; set; }

        [JsonProperty]
        public string User_Hash { get; set; }

        [JsonProperty]
        public string Type { get; set; }

        public string ToJsonString()
        {
            return JsonConvert.SerializeObject(this);
        }
        public static SubscribeRequest Deserialize(string jsonString)
        {
            return JsonConvert.DeserializeObject<SubscribeRequest>(jsonString);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class MeetingConfig
    {
        [JsonProperty]
        public string RoomId { get; set; }

        [JsonProperty]
        public string To { get; set; }

        [JsonProperty]
        public string Url { get; set; }

        [JsonProperty]
        public string User_Hash { get; set; }
    }
}

/*
{
————"roomid": "9820",
————"user_hash": "47407",
————"broadcasting": true,
————"userid": "47407"
} 

"{"roomid":"14293","user_hash":"12787","broadcasting":true,"userid":"12787"}"

"{"roomid":"14293","user_hash":"12787","broadcasting":true,"userid":"12787"}"
*/