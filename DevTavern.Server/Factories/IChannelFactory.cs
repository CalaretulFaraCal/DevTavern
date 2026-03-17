using DevTavern.Server.Models;

namespace DevTavern.Server.Factories
{
    public interface IChannelFactory
    {
        Channel CreateProjectChannel(int projectId);
        Channel CreateOffTopicChannel(int projectId);
    }
}
