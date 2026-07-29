using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace KingdomRuler.Core
{
    public sealed class GameEntryPoint : IStartable
    {
        public void Start()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Additive);
        }
    }
}
