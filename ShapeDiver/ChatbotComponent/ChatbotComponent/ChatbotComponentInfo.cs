using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace ChatbotComponent
{
    public class ChatbotComponentInfo : GH_AssemblyInfo
    {
        public override string Name => "ChatbotComponent";

        internal static readonly Bitmap _icon = LoadIcon();

        private static Bitmap LoadIcon()
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("ChatbotComponent.glean_bot.jfif");
            if (stream == null) return null;
            var src = new Bitmap(stream);
            return new Bitmap(src, new System.Drawing.Size(24, 24));
        }

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => _icon;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => $"Glean AI chatbot component for ShapeDiver apps. v{GleanChatbotComponent.Version}";

        public override Guid Id => new Guid("ab953f82-966b-46ce-a2f3-75908cb7cf4e");

        //Return a string identifying you or your company.
        public override string AuthorName => "Jason Yang";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "xyang@thorntontomasetti.com";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}

