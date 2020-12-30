using System.Text;

namespace LeiKaiFeng.Http
{
    public abstract class MHttpMessage
    {
        protected MHttpMessage(MHttpHeaders headers, MHttpContent content)
        {
            Headers = headers;
        
            Content = content;
        }

        public MHttpHeaders Headers { get; private set; }

        public MHttpContent Content { get; private set; }


        public void SetContent(string s)
        {
            SetContent(Encoding.UTF8.GetBytes(s));
        }


        public void SetContent(byte[] array)
        {
            Headers.SetContentLength(array.Length);

            Headers.RemoveContentEncoding();

            Content.SetByteArray(array);
        }

    }
}