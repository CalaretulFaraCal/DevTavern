using DevTavern.Server.Models;

namespace DevTavern.Server.Factories
{
    public class ChannelFactory : IChannelFactory
    {
        public Channel CreateProjectChannel(int projectId)
        {
            return new Channel
            {
                Name = "general-tech",
                Type = ChannelType.Project,
                ProjectId = projectId
            };
        }

        public Channel CreateOffTopicChannel(int projectId)
        {
            return new Channel
            {
                Name = "off-topic-lounge",
                Type = ChannelType.OffTopic,
                ProjectId = projectId
            };
        }
    }
}
