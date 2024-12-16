using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GustUI
{
    public static class UIFont
    {
        public static String Icon(this Symbol symbol)
        {
            return Char.ConvertFromUtf32((int)symbol);
        }
        public enum Symbol
        {
            //
            // Summary:
            //     E892 <img alt="Previous icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e892.png"
            //     align="top" />
            Previous = 57600,
            //
            // Summary:
            //     E893 <img alt="Next icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e893.png"
            //     />
            Next = 57601,
            //
            // Summary:
            //     E768 <img alt="Play icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e768.png"
            //     />
            Play = 57602,
            //
            // Summary:
            //     E769 <img alt="Pause icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e769.png"
            //     />
            Pause = 57603,
            //
            // Summary:
            //     E70F <img alt="Edit icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e70f.png"
            //     />
            Edit = 57604,
            //
            // Summary:
            //     E74E <img alt="Save icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e74e.png"
            //     />
            Save = 57605,
            //
            // Summary:
            //     E894 <img alt="Clear icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e894.png"
            //     />
            Clear = 57606,
            //
            // Summary:
            //     E74D <img alt="Delete icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e74d.png"
            //     />
            Delete = 57607,
            //
            // Summary:
            //     E738 <img alt="Remove icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e738.png"
            //     />
            Remove = 57608,
            //
            // Summary:
            //     E710 <img alt="Add icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e710.png"
            //     />
            Add = 57609,
            //
            // Summary:
            //     E711 <img alt="Cancel icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e711.png"
            //     />
            Cancel = 57610,
            //
            // Summary:
            //     E8FB <img alt="Accept icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8fb.png"
            //     />
            Accept = 57611,
            //
            // Summary:
            //     E712 <img alt="More icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e712.png"
            //     />
            More = 57612,
            //
            // Summary:
            //     E7A6 <img alt="Redo icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7a6.png"
            //     />
            Redo = 57613,
            //
            // Summary:
            //     E7A7 <img alt="Undo icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7a7.png"
            //     />
            Undo = 57614,
            //
            // Summary:
            //     E80F <img alt="Home icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e80f.png"
            //     />
            Home = 57615,
            //
            // Summary:
            //     E74A <img alt="Up icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e74a.png"
            //     />
            Up = 57616,
            //
            // Summary:
            //     E72A <img alt="Forward icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e72a.png"
            //     />
            Forward = 57617,
            //
            // Summary:
            //     E72B <img alt="Back icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e72b.png"
            //     />
            Back = 57618,
            //
            // Summary:
            //     E734 <img alt="Favorite icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e734.png"
            //     />
            Favorite = 57619,
            //
            // Summary:
            //     E722 <img alt="Camera icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e722.png"
            //     />
            Camera = 57620,
            //
            // Summary:
            //     E713 <img alt="Setting icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e713.png"
            //     />
            Setting = 57621,
            //
            // Summary:
            //     E714 <img alt="Video icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e714.png"
            //     />
            Video = 57622,
            //
            // Summary:
            //     E895 <img alt="Sync icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e895.png"
            //     />
            Sync = 57623,
            //
            // Summary:
            //     E896 <img alt="Download icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e896.png"
            //     />
            Download = 57624,
            //
            // Summary:
            //     E715 <img alt="Mail icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e715.png"
            //     />
            Mail = 57625,
            //
            // Summary:
            //     E721 <img alt="Find icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e721.png"
            //     />
            Find = 57626,
            //
            // Summary:
            //     E897 <img alt="Help icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e897.png"
            //     />
            Help = 57627,
            //
            // Summary:
            //     E898 <img alt="Upload icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e898.png"
            //     />
            Upload = 57628,
            //
            // Summary:
            //     E899 <img alt="Emoji icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e899.png"
            //     />
            Emoji = 57629,
            //
            // Summary:
            //     E89A <img alt="Two Page icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e89a.png"
            //     />
            TwoPage = 57630,
            //
            // Summary:
            //     E89B <img alt="Leave Chat icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e89b.png"
            //     />
            LeaveChat = 57631,
            //
            // Summary:
            //     E89C <img alt="Mail Forward icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e89c.png"
            //     />
            MailForward = 57632,
            //
            // Summary:
            //     E823 <img alt="Clock icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e823.png"
            //     />
            Clock = 57633,
            //
            // Summary:
            //     E724 <img alt="Send icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e724.png"
            //     />
            Send = 57634,
            //
            // Summary:
            //     E7A8 <img alt="Crop icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7a8.png"
            //     />
            Crop = 57635,
            //
            // Summary:
            //     E89E <img alt="Rotate Camera icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e89e.png"
            //     />
            RotateCamera = 57636,
            //
            // Summary:
            //     E716 <img alt="People icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e716.png"
            //     />
            People = 57637,
            //
            // Summary:
            //     E8A0 <img alt="Open Pane icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a0.png"
            //     />
            OpenPane = 57638,
            //
            // Summary:
            //     E89F <img alt="Close Pane icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e89f.png"
            //     />
            ClosePane = 57639,
            //
            // Summary:
            //     E909 <img alt="World icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e909.png"
            //     />
            World = 57640,
            //
            // Summary:
            //     E7C1 <img alt="Flag icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7c1.png"
            //     />
            Flag = 57641,
            //
            // Summary:
            //     E8A1 <img alt="Preview Link icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a1.png"
            //     />
            PreviewLink = 57642,
            //
            // Summary:
            //     E774 <img alt="Globe icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e774.png"
            //     />
            Globe = 57643,
            //
            // Summary:
            //     E78A <img alt="Trim icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e78a.png"
            //     />
            Trim = 57644,
            //
            // Summary:
            //     E8A2 <img alt="Attach Camera icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a2.png"
            //     />
            AttachCamera = 57645,
            //
            // Summary:
            //     E8A3 <img alt="Zoom In icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a3.png"
            //     />
            ZoomIn = 57646,
            //
            // Summary:
            //     E8A4 <img alt="Bookmarks icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a4.png"
            //     />
            Bookmarks = 57647,
            //
            // Summary:
            //     E8A5 <img alt="Document icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a5.png"
            //     />
            Document = 57648,
            //
            // Summary:
            //     E8A6 <img alt="Protected Document icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a6.png"
            //     />
            ProtectedDocument = 57649,
            //
            // Summary:
            //     E729 <img alt="Page icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e729.png"
            //     />
            Page = 57650,
            //
            // Summary:
            //     E8FD <img alt="Bullets icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8fd.png"
            //     />
            Bullets = 57651,
            //
            // Summary:
            //     E90A <img alt="Comment icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90a.png"
            //     />
            Comment = 57652,
            //
            // Summary:
            //     E8A8 <img alt="Mail Filled icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a8.png"
            //     />
            MailFilled = 57653,
            //
            // Summary:
            //     E779 <img alt="Contact Info icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e779.png"
            //     />
            ContactInfo = 57654,
            //
            // Summary:
            //     E778 <img alt="Hang Up icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e778.png"
            //     />
            HangUp = 57655,
            //
            // Summary:
            //     E8A9 <img alt="View All icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8a9.png"
            //     />F
            ViewAll = 57656,
            //
            // Summary:
            //     E7B7 <img alt="Map Pin icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7b7.png"
            //     />
            MapPin = 57657,
            //
            // Summary:
            //     E717 <img alt="Phone icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e717.png"
            //     />
            Phone = 57658,
            //
            // Summary:
            //     E8AA <img alt="Video Chat icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8aa.png"
            //     />
            VideoChat = 57659,
            //
            // Summary:
            //     E8AB <img alt="Switch icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ab.png"
            //     />
            Switch = 57660,
            //
            // Summary:
            //     E77B <img alt="Contact icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e77b.png"
            //     />
            Contact = 57661,
            //
            // Summary:
            //     E8AC <img alt="Rename icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ac.png"
            //     />
            Rename = 57662,
            //
            // Summary:
            //     E718 <img alt="Pin icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e718.png"
            //     />
            Pin = 57665,
            //
            // Summary:
            //     E90B <img alt="Music Info icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90b.png"
            //     />
            MusicInfo = 57666,
            //
            // Summary:
            //     E8AD <img alt="Go icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ad.png"
            //     />
            Go = 57667,
            //
            // Summary:
            //     E765 <img alt="Keyboard icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e765.png"
            //     />
            Keyboard = 57668,
            //
            // Summary:
            //     E90C <img alt="Dock Left icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90c.png"
            //     />
            DockLeft = 57669,
            //
            // Summary:
            //     E90D <img alt="Dock Right icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90d.png"
            //     />
            DockRight = 57670,
            //
            // Summary:
            //     E90E <img alt="Dock Bottom icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90e.png"
            //     />
            DockBottom = 57671,
            //
            // Summary:
            //     E8AF <img alt="Remote icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8af.png"
            //     />
            Remote = 57672,
            //
            // Summary:
            //     E72C <img alt="Refresh icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e72c.png"
            //     />
            Refresh = 57673,
            //
            // Summary:
            //     E7AD <img alt="Rotate icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7ad.png"
            //     />
            Rotate = 57674,
            //
            // Summary:
            //     E8B1 <img alt="Shuffle icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b1.png"
            //     />
            Shuffle = 57675,
            //
            // Summary:
            //     EA37 <img alt="List icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/ea37.png"
            //     />
            List = 57676,
            //
            // Summary:
            //     E719 <img alt="Shop icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e719.png"
            //     />
            Shop = 57677,
            //
            // Summary:
            //     E8B3 <img alt="Select All icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b3.png"
            //     />
            SelectAll = 57678,
            //
            // Summary:
            //     E8B4 <img alt="Orientation icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b4.png"
            //     />
            Orientation = 57679,
            //
            // Summary:
            //     E8B5 <img alt="Import icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b5.png"
            //     />
            Import = 57680,
            //
            // Summary:
            //     E8B6 <img alt="Import All icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b6.png"
            //     />
            ImportAll = 57681,
            //
            // Summary:
            //     E7C5 <img alt="Browse Photos icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7c5.png"
            //     />
            BrowsePhotos = 57685,
            //
            // Summary:
            //     E8B8 <img alt="Web Cam icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b8.png"
            //     />
            WebCam = 57686,
            //
            // Summary:
            //     E8B9 <img alt="Pictures icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b9.png"
            //     />
            Pictures = 57688,
            //
            // Summary:
            //     E78C <img alt="Save Local icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e78c.png"
            //     />
            SaveLocal = 57689,
            //
            // Summary:
            //     E8BA <img alt="Caption icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ba.png"
            //     />
            Caption = 57690,
            //
            // Summary:
            //     E71A <img alt="Stop icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71a.png"
            //     />
            Stop = 57691,
            //
            // Summary:
            //     E8BC <img alt="Show Results icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8bc.png"
            //     />
            ShowResults = 57692,
            //
            // Summary:
            //     E767 <img alt="Volume icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e767.png"
            //     />
            Volume = 57693,
            //
            // Summary:
            //     E90F <img alt="Repair icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e90f.png"
            //     />
            Repair = 57694,
            //
            // Summary:
            //     E8BD <img alt="Message icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8bd.png"
            //     />
            Message = 57695,
            //
            // Summary:
            //     E7C3 <img alt="Page2 icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7c3.png"
            //     />
            Page2 = 57696,
            //
            // Summary:
            //     E8BF <img alt="Calendar Day icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8bf.png"
            //     />
            CalendarDay = 57697,
            //
            // Summary:
            //     E8C0 <img alt="Calendar Week icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c0.png"
            //     />
            CalendarWeek = 57698,
            //
            // Summary:
            //     E787 <img alt="Calendar icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e787.png"
            //     />
            Calendar = 57699,
            //
            // Summary:
            //     E8C1 <img alt="Character icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c1.png"
            //     />
            Character = 57700,
            //
            // Summary:
            //     E8C2 <img alt="Mail Reply All icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c2.png"
            //     />
            MailReplyAll = 57701,
            //
            // Summary:
            //     E8C3 <img alt="Read icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c3.png"
            //     />
            Read = 57702,
            //
            // Summary:
            //     E71B <img alt="Link icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71b.png"
            //     />
            Link = 57703,
            //
            // Summary:
            //     E910 <img alt="Account icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e910.png"
            //     />
            Account = 57704,
            //
            // Summary:
            //     E8C4 <img alt="Show BCC icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c4.png"
            //     />
            ShowBcc = 57705,
            //
            // Summary:
            //     E8C5 <img alt="Hide BCC icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c5.png"
            //     />
            HideBcc = 57706,
            //
            // Summary:
            //     E8C6 <img alt="Cut icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c6.png"
            //     />
            Cut = 57707,
            //
            // Summary:
            //     E723 <img alt="Attach icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e723.png"
            //     />
            Attach = 57708,
            //
            // Summary:
            //     E77F <img alt="Paste icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e77f.png"
            //     />
            Paste = 57709,
            //
            // Summary:
            //     E71C <img alt="Filter icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71c.png"
            //     />
            Filter = 57710,
            //
            // Summary:
            //     E8C8 <img alt="Copy icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c8.png"
            //     />
            Copy = 57711,
            //
            // Summary:
            //     E76E <img alt="Emoji 2 icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e76e.png"
            //     />
            Emoji2 = 57712,
            //
            // Summary:
            //     E8C9 <img alt="Important icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8c9.png"
            //     />
            Important = 57713,
            //
            // Summary:
            //     E8CA <img alt="Mail Reply icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ca.png"
            //     />
            MailReply = 57714,
            //
            // Summary:
            //     E786 <img alt="Slide Show icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e786.png"
            //     />
            SlideShow = 57715,
            //
            // Summary:
            //     E8CB <img alt="Sort icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8cb.png"
            //     />
            Sort = 57716,
            //
            // Summary:
            //     E912 <img alt="Manage icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e912.png"
            //     />
            Manage = 57720,
            //
            // Summary:
            //     E71D <img alt="All Apps icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71d.png"
            //     />
            AllApps = 57721,
            //
            // Summary:
            //     E8CD <img alt="Disconnect Drive icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8cd.png"
            //     />
            DisconnectDrive = 57722,
            //
            // Summary:
            //     E8CE <img alt="Map Drive icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ce.png"
            //     />
            MapDrive = 57723,
            //
            // Summary:
            //     E78B <img alt="New Window icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e78b.png"
            //     />
            NewWindow = 57724,
            //
            // Summary:
            //     E7AC <img alt="Open With icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7ac.png"
            //     />
            OpenWith = 57725,
            //
            // Summary:
            //     E8CF <img alt="Contact Presence icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8cf.png"
            //     />
            ContactPresence = 57729,
            //
            // Summary:
            //     E8D0 <img alt="Priority icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d0.png"
            //     />
            Priority = 57730,
            //
            // Summary:
            //     E8D1 <img alt="Go To Today icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d1.png"
            //     />
            GoToToday = 57732,
            //
            // Summary:
            //     E8D2 <img alt="Font icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d2.png"
            //     />
            Font = 57733,
            //
            // Summary:
            //     E8D3 <img alt="Font Color icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d3.png"
            //     />
            FontColor = 57734,
            //
            // Summary:
            //     E8D4 <img alt="Contact 2 icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d4.png"
            //     />
            Contact2 = 57735,
            //
            // Summary:
            //     E8B7 <img alt="Folder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8b7.png"
            //     />
            Folder = 57736,
            //
            // Summary:
            //     E8D6 <img alt="Audio icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d6.png"
            //     />
            Audio = 57737,
            //
            // Summary:
            //     E18A <img alt="Placeholder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e18a.png"
            //     />
            Placeholder = 57738,
            //
            // Summary:
            //     E890 <img alt="View icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e890.png"
            //     />
            View = 57739,
            //
            // Summary:
            //     E7B5 <img alt="Set Lock Screen icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7b5.png"
            //     />
            SetLockScreen = 57740,
            //
            // Summary:
            //     E97B <img alt="Set Tile icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e97b.png"
            //     />
            SetTile = 57741,
            //
            // Summary:
            //     E7F0 <img alt="Closed Caption icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7f0.png"
            //     />
            ClosedCaption = 57744,
            //
            // Summary:
            //     E620 <img alt="Stop Slide Show icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e620.png"
            //     />
            StopSlideShow = 57745,
            //
            // Summary:
            //     E8D7 <img alt="Permissions icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d7.png"
            //     />
            Permissions = 57746,
            //
            // Summary:
            //     E7E6 <img alt="Highlight icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7e6.png"
            //     />
            Highlight = 57747,
            //
            // Summary:
            //     E8D8 <img alt="Disable Updates icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d8.png"
            //     />
            DisableUpdates = 57748,
            //
            // Summary:
            //     E8D9 <img alt="Unfavorite icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8d9.png"
            //     />
            UnFavorite = 57749,
            //
            // Summary:
            //     E77A <img alt="UnPin icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e77a.png"
            //     />
            UnPin = 57750,
            //
            // Summary:
            //     E8DA <img alt="Open Local icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8da.png"
            //     />
            OpenLocal = 57751,
            //
            // Summary:
            //     E74F <img alt="Mute icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e74f.png"
            //     />
            Mute = 57752,
            //
            // Summary:
            //     E8DB <img alt="Italic icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8db.png"
            //     />
            Italic = 57753,
            //
            // Summary:
            //     E8DC <img alt="Underline icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8dc.png"
            //     />
            Underline = 57754,
            //
            // Summary:
            //     E8DD <img alt="Bold icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8dd.png"
            //     />
            Bold = 57755,
            //
            // Summary:
            //     E8DE <img alt="Move To Folder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8de.png"
            //     />
            MoveToFolder = 57756,
            //
            // Summary:
            //     E8DF <img alt="Like Dislike icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8df.png"
            //     />
            LikeDislike = 57757,
            //
            // Summary:
            //     E8E0 <img alt="Dislike icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e0.png"
            //     />
            Dislike = 57758,
            //
            // Summary:
            //     E8E1 <img alt="Like icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e1.png"
            //     />
            Like = 57759,
            //
            // Summary:
            //     E8E2 <img alt="Align Right icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e2.png"
            //     />
            AlignRight = 57760,
            //
            // Summary:
            //     E8E3 <img alt="Align Center icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e3.png"
            //     />
            AlignCenter = 57761,
            //
            // Summary:
            //     E8E4 <img alt="Align Left icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e4.png"
            //     />
            AlignLeft = 57762,
            //
            // Summary:
            //     E71E <img alt="Zoom icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71e.png"
            //     />
            Zoom = 57763,
            //
            // Summary:
            //     E71F <img alt="Zoom Out icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e71f.png"
            //     />
            ZoomOut = 57764,
            //
            // Summary:
            //     E8E5 <img alt="Open File icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e5.png"
            //     />
            OpenFile = 57765,
            //
            // Summary:
            //     E7EE <img alt="Other User icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7ee.png"
            //     />
            OtherUser = 57766,
            //
            // Summary:
            //     E7EF <img alt="Admin icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7ef.png"
            //     />
            Admin = 57767,
            //
            // Summary:
            //     E913 <img alt="Street icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e913.png"
            //     />
            Street = 57795,
            //
            // Summary:
            //     E707 <img alt="Map icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e707.png"
            //     />
            Map = 57796,
            //
            // Summary:
            //     E8E6 <img alt="Clear Selection icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e6.png"
            //     />
            ClearSelection = 57797,
            //
            // Summary:
            //     E8E7 <img alt="Font Decrease icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e7.png"
            //     />
            FontDecrease = 57798,
            //
            // Summary:
            //     E8E8 <img alt="Font Increase icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e8.png"
            //     />
            FontIncrease = 57799,
            //
            // Summary:
            //     E8E9 <img alt="Font Size icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8e9.png"
            //     />
            FontSize = 57800,
            //
            // Summary:
            //     E8EA <img alt="Cell Phone icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ea.png"
            //     />
            CellPhone = 57801,
            //
            // Summary:
            //     E8EB <img alt="Reshare icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8eb.png"
            //     />
            ReShare = 57802,
            //
            // Summary:
            //     E8EC <img alt="Tag icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ec.png"
            //     />
            Tag = 57803,
            //
            // Summary:
            //     E8ED <img alt="Repeat 1 icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ed.png"
            //     />
            RepeatOne = 57804,
            //
            // Summary:
            //     E8EE <img alt="Repeat All icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ee.png"
            //     />
            RepeatAll = 57805,
            //
            // Summary:
            //     E734 <img alt="Outline Star icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e734.png"
            //     />
            OutlineStar = 57806,
            //
            // Summary:
            //     E735 <img alt="Solid Star icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e735.png"
            //     />
            SolidStar = 57807,
            //
            // Summary:
            //     E8EF <img alt="Calculator icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ef.png"
            //     />
            Calculator = 57808,
            //
            // Summary:
            //     E8F0 <img alt="Directions icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f0.png"
            //     />
            Directions = 57809,
            //
            // Summary:
            //     F5F0
            Target = 57810,
            //
            // Summary:
            //     E8F1 <img alt="Library icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f1.png"
            //     />
            Library = 57811,
            //
            // Summary:
            //     E780 <img alt="Phone Book icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e780.png"
            //     />
            PhoneBook = 57812,
            //
            // Summary:
            //     E77C <img alt="Memo icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e77c.png"
            //     />
            Memo = 57813,
            //
            // Summary:
            //     E720 <img alt="Microphone icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e720.png"
            //     />
            Microphone = 57814,
            //
            // Summary:
            //     E8F3 <img alt="Post Update icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f3.png"
            //     />
            PostUpdate = 57815,
            //
            // Summary:
            //     E73F <img alt="Back To Window icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e73f.png"
            //     />
            BackToWindow = 57816,
            //
            // Summary:
            //     E8F3 <img alt="Full Screen icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f3.png"
            //     />
            FullScreen = 57817,
            //
            // Summary:
            //     E8F4 <img alt="New Folder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f4.png"
            //     />
            NewFolder = 57818,
            //
            // Summary:
            //     E8F5 <img alt="Calendar Reply icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f5.png"
            //     />
            CalendarReply = 57819,
            //
            // Summary:
            //     E8F6 <img alt="Unsync Folder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f6.png"
            //     />
            UnSyncFolder = 57821,
            //
            // Summary:
            //     E730 <img alt="Report Hacked icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e730.png"
            //     />
            ReportHacked = 57822,
            //
            // Summary:
            //     E8F7 <img alt="Sync Folder icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f7.png"
            //     />
            SyncFolder = 57823,
            //
            // Summary:
            //     E8F8 <img alt="Block Contact icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f8.png"
            //     />
            BlockContact = 57824,
            //
            // Summary:
            //     E8F9 <img alt="Switch Apps icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8f9.png"
            //     />
            SwitchApps = 57825,
            //
            // Summary:
            //     E8FA <img alt="Add Friend icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8fa.png"
            //     />
            AddFriend = 57826,
            //
            // Summary:
            //     E7C9 <img alt="Touch Pointer icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e7c9.png"
            //     />
            TouchPointer = 57827,
            //
            // Summary:
            //     E8FC <img alt="Go To Start icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8fc.png"
            //     />
            GoToStart = 57828,
            //
            // Summary:
            //     E904 <img alt="Zero Bars icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e904.png"
            //     />
            ZeroBars = 57829,
            //
            // Summary:
            //     E905 <img alt="One Bar icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e905.png"
            //     />
            OneBar = 57830,
            //
            // Summary:
            //     E906 <img alt="Two Bars icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e906.png"
            //     />
            TwoBars = 57831,
            //
            // Summary:
            //     E907 <img alt="Three Bars icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e907.png"
            //     />
            ThreeBars = 57832,
            //
            // Summary:
            //     E908 <img alt="Four Bars icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e908.png"
            //     />
            FourBars = 57833,
            //
            // Summary:
            //     E8FE <img alt="Scan icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8fe.png"
            //     />
            Scan = 58004,
            //
            // Summary:
            //     E8FF <img alt="Preview icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e8ff.png"
            //     />
            Preview = 58005,
            //
            // Summary:
            //     E700 <img alt="GlobalNav icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e700.png"
            //     />
            GlobalNavigationButton = 59136,
            //
            // Summary:
            //     E72D <img alt="Share icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e72d.png"
            //     />
            Share = 59181,
            //
            // Summary:
            //     E749 <img alt="Print icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e749.png"
            //     />
            Print = 59209,
            //
            // Summary:
            //     E990 <img alt="Xbox icon" src="./microsoft.ui.xaml.controls/images/segoe-fluent-icons/e990.png"
            //     />
            XboxOneConsole = 59792
        }
    }
}
