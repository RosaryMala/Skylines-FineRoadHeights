using ICities;

namespace FineRoadHeights
{
    public class FineRoadHeightsMod : IUserMod
    {
        public string Description
        {
            get { return "Decreases the road elevation step size from 12m to 3m"; }
        }

        public string Name
        {
            get { return "Fine Road Heights"; }
        }
    }
}
