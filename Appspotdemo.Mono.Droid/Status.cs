using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace Appspotdemo.Mono.Droid
{
    class Status
    {
        public const string Status00 = "Capturing webcam......";
        public const string Status01 = "Room is created. Searching participant ...";
        public const string Status02 = "Joining room....";
        public const string Status03 = "Room found, connecting...";
        public const string Status04 = "Participant found, connecting...";
        public const string Status05 = "You have left the call.";
        public const string Status06 = "Waiting for someone to join...";
        public const string Status07 = "Initializing...";
        public const string Status08 = "<input type=\'button\' id=\'hangup\' value=\'Hang up\' onclick=\'onHangup()\' />";
        public const string Status09 = "Room is full.";
        public const string Status10 = "The other participant has left the meeting. This meeting has ended.";
    }
}