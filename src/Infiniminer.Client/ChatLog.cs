namespace Infiniminer
{
    public class ChatMessage
    {
        public string message;
        public ChatMessageType type;
        public float timestamp;     // seconds left on screen

        public ChatMessage(string message, ChatMessageType type, float timestamp)
        {
            this.message = message;
            this.type = type;
            this.timestamp = timestamp;
        }
    }

    // The chat lines on screen, newest first. Each one fades out after a while.
    public class ChatLog
    {
        public const float Lifetime = 10;

        readonly List<ChatMessage> messages = new List<ChatMessage>();

        public IReadOnlyList<ChatMessage> Messages
        {
            get { return messages; }
        }

        public void Add(string text, ChatMessageType type)
        {
            messages.Insert(0, new ChatMessage(text, type, Lifetime));
        }

        public void Update(float elapsedSeconds)
        {
            foreach (ChatMessage message in messages)
                message.timestamp -= elapsedSeconds;
            messages.RemoveAll(message => message.timestamp <= 0);
        }

        public void Clear()
        {
            messages.Clear();
        }
    }
}
