using LiteNetLib.Utils;
using Microsoft.Xna.Framework;

namespace Infiniminer
{
    // Applies the messages the server sends to the client's view of the game.
    public class ServerMessageHandler
    {
        readonly InfiniminerGame game;

        public ServerMessageHandler(InfiniminerGame game)
        {
            this.game = game;
        }

        PropertyBag _P
        {
            get { return game.propertyBag; }
        }

        public void Handle(NetDataReader msgBuffer, GameTime gameTime)
        {
            InfiniminerMessage dataType = (InfiniminerMessage)msgBuffer.GetByte();
            switch (dataType)
            {
                case InfiniminerMessage.WorldInfo:
                    {
                        int size = msgBuffer.GetUShort();
                        if (!GameConstants.MapSizes.Contains(size))
                            throw new InvalidDataException("unsupported map size " + size);
                        // A fresh world: on joining, or when the server loads a different map.
                        _P.blockEngine.Dispose();
                        _P.blockEngine = new BlockEngine(game, size);
                        _P.mapSize = size;
                        _P.mapLoadProgress = new bool[size, size];
                        _P.downloadComplete = false;
                        _P.beaconList.Clear();
                    }
                    break;

                case InfiniminerMessage.BlockBulkTransfer:
                    {
                        int size = _P.mapSize;
                        byte x = msgBuffer.GetByte();
                        byte y = msgBuffer.GetByte();
                        byte[] rows = MapChunk.Decompress(msgBuffer.GetRemainingBytes(), 16 * size);
                        _P.mapLoadProgress[x, y] = true;
                        for (int dy = 0; dy < 16; dy++)
                            for (int z = 0; z < size; z++)
                            {
                                BlockType blockType = (BlockType)rows[dy * size + z];
                                if (blockType != BlockType.None && blockType < BlockType.MAXIMUM)
                                    _P.blockEngine.downloadList[x, y + dy, z] = blockType;
                            }
                        bool downloadComplete = true;
                        for (int cx = 0; cx < size && downloadComplete; cx++)
                            for (int cy = 0; cy < size; cy += 16)
                                if (_P.mapLoadProgress[cx, cy] == false)
                                {
                                    downloadComplete = false;
                                    break;
                                }
                        if (downloadComplete && !_P.downloadComplete)
                        {
                            _P.downloadComplete = true;
                            if (game.CurrentStateType == typeof(States.LoadingState))
                                game.ChangeStateAfterNetworkUpdate(typeof(States.TeamSelectionState));
                            game.TitleMusic.Stop();
                            _P.blockEngine.DownloadComplete();
                        }
                    }
                    break;

                case InfiniminerMessage.SetBeacon:
                    {
                        Vector3 position = msgBuffer.GetXnaVector3();
                        string text = msgBuffer.GetString();
                        PlayerTeam team = (PlayerTeam)msgBuffer.GetByte();

                        if (text == "")
                        {
                            if (_P.beaconList.ContainsKey(position))
                                _P.beaconList.Remove(position);
                        }
                        else
                        {
                            Beacon newBeacon = new Beacon();
                            newBeacon.ID = text;
                            newBeacon.Team = team;
                            _P.beaconList[position] = newBeacon;
                        }
                    }
                    break;

                case InfiniminerMessage.TriggerConstructionGunAnimation:
                    {
                        _P.constructionGunAnimation = msgBuffer.GetFloat();
                        if (_P.constructionGunAnimation <= -0.1)
                        {
                            _P.PlaySound(InfiniminerSound.RadarSwitch);
                            _P.blockPredictor.OnBuildFailed();
                        }
                    }
                    break;

                case InfiniminerMessage.ResourceUpdate:
                    {
                        // ore, cash, weight, max ore, max weight, team ore, red cash, blue cash, all uint
                        _P.playerOre = msgBuffer.GetUInt();
                        _P.playerCash = msgBuffer.GetUInt();
                        _P.playerWeight = msgBuffer.GetUInt();
                        _P.playerOreMax = msgBuffer.GetUInt();
                        _P.playerWeightMax = msgBuffer.GetUInt();
                        _P.teamOre = msgBuffer.GetUInt();
                        _P.teamRedCash = msgBuffer.GetUInt();
                        _P.teamBlueCash = msgBuffer.GetUInt();
                    }
                    break;

                case InfiniminerMessage.BlockSet:
                    {
                        // x, y, z, type, all bytes
                        byte x = msgBuffer.GetByte();
                        byte y = msgBuffer.GetByte();
                        byte z = msgBuffer.GetByte();
                        BlockType blockType = (BlockType)msgBuffer.GetByte();
                        if (blockType >= BlockType.MAXIMUM)
                            break;
                        _P.blockPredictor.OnServerBlockSet(x, y, z);
                        if (blockType == BlockType.None)
                        {
                            if (_P.blockEngine.BlockAtPoint(new Vector3(x, y, z)) != BlockType.None)
                                _P.blockEngine.RemoveBlock(x, y, z);
                        }
                        else
                        {
                            if (_P.blockEngine.BlockAtPoint(new Vector3(x, y, z)) != BlockType.None)
                                _P.blockEngine.RemoveBlock(x, y, z);
                            _P.blockEngine.AddBlock(x, y, z, blockType);
                            CheckForStandingInLava();
                        }
                    }
                    break;

                case InfiniminerMessage.TriggerExplosion:
                    {
                        Vector3 blockPos = msgBuffer.GetXnaVector3();

                        // Play the explosion sound.
                        _P.PlaySound(InfiniminerSound.Explosion, blockPos);

                        // Create some particles.
                        _P.particleEngine.CreateExplosionDebris(blockPos);

                        // Figure out what the effect is.
                        float distFromExplosive = (blockPos + 0.5f * Vector3.One - _P.playerPosition).Length();
                        if (distFromExplosive < 3)
                            _P.KillPlayer("WAS KILLED IN AN EXPLOSION!");
                        else if (distFromExplosive < 8)
                        {
                            // If we're not in explosion mode, turn it on with the minimum ammount of shakiness.
                            if (_P.screenEffect != ScreenEffect.Explosion)
                            {
                                _P.screenEffect = ScreenEffect.Explosion;
                                _P.screenEffectCounter = 2;
                            }
                            // If this bomb would result in a bigger shake, use its value.
                            _P.screenEffectCounter = Math.Min(_P.screenEffectCounter, (distFromExplosive - 2) / 5);
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerSetTeam:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                        {
                            Player player = _P.playerList[playerId];
                            player.Team = (PlayerTeam)msgBuffer.GetByte();

                            // The server can put us on the other team (auto-balance).
                            if (playerId == _P.playerMyId && player.Team != PlayerTeam.None && player.Team != _P.playerTeam)
                            {
                                _P.playerTeam = player.Team;
                                _P.playerBlocks = GameRules.GetBlocks(_P.playerClass, player.Team);
                                _P.playerBlockSelected = 0;
                            }
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerJoined:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        string playerName = msgBuffer.GetString();
                        bool thisIsMe = msgBuffer.GetBool();
                        bool playerAlive = msgBuffer.GetBool();
                        _P.playerList[playerId] = new Player(game);
                        _P.playerList[playerId].Handle = InfiniminerGame.Sanitize(playerName);
                        _P.playerList[playerId].ID = playerId;
                        _P.playerList[playerId].Alive = playerAlive;
                        if (thisIsMe)
                            _P.playerMyId = playerId;
                    }
                    break;

                case InfiniminerMessage.PlayerLeft:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                            _P.playerList.Remove(playerId);
                    }
                    break;

                case InfiniminerMessage.PlayerDead:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                        {
                            Player player = _P.playerList[playerId];
                            player.Alive = false;
                            if (playerId == _P.playerMyId)
                                _P.EnterDeathState();
                            _P.particleEngine.CreateBloodSplatter(player.Position, player.Team == PlayerTeam.Red ? Color.Red : Color.Blue);
                            if (playerId != _P.playerMyId)
                                _P.PlaySound(InfiniminerSound.Death, player.Position);
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerAlive:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                        {
                            Player player = _P.playerList[playerId];
                            player.Alive = true;
                            if (playerId == _P.playerMyId)
                                _P.OnRespawned();
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerUpdate:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                        {
                            Player player = _P.playerList[playerId];
                            player.UpdatePosition(msgBuffer.GetXnaVector3(), gameTime.TotalGameTime.TotalSeconds);
                            player.Heading = msgBuffer.GetXnaVector3();
                            player.Tool = (PlayerTools)msgBuffer.GetByte();
                            player.UsingTool = msgBuffer.GetBool();
                            player.Score = (uint)(msgBuffer.GetUShort() * 100);
                        }
                    }
                    break;

                case InfiniminerMessage.MatchInfo:
                    {
                        int secondsLeft = msgBuffer.GetInt();
                        _P.winningCash = msgBuffer.GetUInt();
                        _P.overtime = secondsLeft == 0;
                        _P.matchEndTime = secondsLeft < 0 ? double.NaN : gameTime.TotalGameTime.TotalSeconds + secondsLeft;
                    }
                    break;

                case InfiniminerMessage.GameOver:
                    {
                        _P.teamWinners = (PlayerTeam)msgBuffer.GetByte();
                    }
                    break;

                case InfiniminerMessage.ChatMessage:
                    {
                        ChatMessageType chatType = (ChatMessageType)msgBuffer.GetByte();
                        string chatString = InfiniminerGame.Sanitize(msgBuffer.GetString());
                        _P.chatLog.Add(chatString, chatType);
                        _P.PlaySound(InfiniminerSound.ClickLow);
                    }
                    break;

                case InfiniminerMessage.PlayerPing:
                    {
                        uint playerId = msgBuffer.GetUInt();
                        if (_P.playerList.ContainsKey(playerId))
                        {
                            if (_P.playerList[playerId].Team == _P.playerTeam)
                            {
                                _P.playerList[playerId].Ping = 1;
                                _P.PlaySound(InfiniminerSound.Ping);
                            }
                        }
                    }
                    break;

                case InfiniminerMessage.PlayerSetPosition:
                    {
                        Vector3 position = msgBuffer.GetXnaVector3();
                        byte epoch = msgBuffer.GetByte();
                        PositionReason reason = (PositionReason)msgBuffer.GetByte();
                        _P.SetPositionFromServer(position, epoch, reason);
                    }
                    break;

                case InfiniminerMessage.PlaySound:
                    {
                        InfiniminerSound sound = (InfiniminerSound)msgBuffer.GetByte();
                        bool hasPosition = msgBuffer.GetBool();
                        if (hasPosition)
                        {
                            Vector3 soundPosition = msgBuffer.GetXnaVector3();
                            _P.PlaySound(sound, soundPosition);
                        }
                        else
                            _P.PlaySound(sound);
                    }
                    break;
            }
        }

        void CheckForStandingInLava()
        {
            // Copied from TryToMoveTo; responsible for checking if lava has flowed over us.

            Vector3 movePosition = _P.playerPosition;
            Vector3 midBodyPoint = movePosition + new Vector3(0, -0.7f, 0);
            Vector3 lowerBodyPoint = movePosition + new Vector3(0, -1.4f, 0);
            BlockType lowerBlock = _P.blockEngine.BlockAtPoint(lowerBodyPoint);
            BlockType midBlock = _P.blockEngine.BlockAtPoint(midBodyPoint);
            BlockType upperBlock = _P.blockEngine.BlockAtPoint(movePosition);
            if (upperBlock == BlockType.Lava || lowerBlock == BlockType.Lava || midBlock == BlockType.Lava)
            {
                _P.KillPlayer("WAS INCINERATED BY LAVA!");
            }
        }
    }
}
