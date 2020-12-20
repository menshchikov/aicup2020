using System.Collections.Generic;
using Aicup2020.Model;

namespace Aicup2020
{
    public class Helper1
    {
        public static void RotateClockwise(ref int x,ref int y)
        {
            var a = $"{x}{y}";
            switch (a)
            {
                case "11":
                {
                    y = 0;
                    break;
                }
                case "10":
                {
                    y = -1;
                    break;
                }
                case "1-1":
                {
                    x = 0;
                    break;
                }
                case "0-1":
                {
                    x = -1;
                    break;
                }
                case "-1-1":
                {
                    y = 0;
                    break;
                }
                case "-10":
                {
                    y = 1;
                    break;
                }
                case "-11":
                {
                    x = 0;
                    break;
                }
                case "01":
                {
                    x = 1;
                    break;
                }
            }
        }

        public static void GetPlayers(PlayerView playerView, ref Player? me, ref List<Player> enemies)
        {
            foreach (var playerViewPlayer in playerView.Players)
            {
                if (playerView.MyId == playerViewPlayer.Id)
                {
                    me = playerViewPlayer;
                }
                else
                {
                    enemies.Add(playerViewPlayer);
                }
            }
        }
    }
}