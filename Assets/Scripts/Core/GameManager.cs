using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Chess.Game {
	public class GameManager : MonoBehaviour {

        #region Library
        public enum Result { Playing, WhiteIsMated, BlackIsMated, Stalemate, Repetition, FiftyMoveRule, InsufficientMaterial }

		public event System.Action onPositionLoaded;
		public event System.Action<Move> onMoveMade;

		public enum PlayerType { Human, AI }

		public bool loadCustomPosition;
		public string customPosition = "1rbq1r1k/2pp2pp/p1n3p1/2b1p3/R3P3/1BP2N2/1P3PPP/1NBQ1RK1 w - - 0 1";

		public PlayerType whitePlayerType;
		public PlayerType blackPlayerType;
		public AISettings aiSettings;
		public Color[] colors;

		public bool useClocks;
		public Clock whiteClock;
		public Clock blackClock;
		public TMPro.TMP_Text aiDiagnosticsUI;
		public TMPro.TMP_Text resultUI;

        public Result gameResult;

		Player whitePlayer;
		Player blackPlayer;
		Player playerToMove;
		List<Move> gameMoves;
		BoardUI boardUI;

		public ulong zobristDebug;
		public Board board { get; private set; }
		Board searchBoard; // Duplicate version of board used for ai search

		public void NewGame (bool humanPlaysWhite) {
			boardUI.SetPerspective (humanPlaysWhite);
			NewGame ((humanPlaysWhite) ? PlayerType.Human : PlayerType.AI, (humanPlaysWhite) ? PlayerType.AI : PlayerType.Human);
		}

		public void NewComputerVersusComputerGame () {
			boardUI.SetPerspective (true);
			NewGame (PlayerType.AI, PlayerType.AI);
		}
        void OnMoveChosen(Move move)
        {
            
            bool animateMove = playerToMove is AIPlayer;
            board.MakeMove(move);
            searchBoard.MakeMove(move);

            gameMoves.Add(move);
            onMoveMade?.Invoke(move);
            boardUI.OnMoveMade(board, move, animateMove);

            NotifyPlayerToMove();
            StartCoroutine(SendMoveToGameServerCoroutine(move.Name));
        }
        void NewGame (PlayerType whitePlayerType, PlayerType blackPlayerType) {
			gameMoves.Clear ();
			if (loadCustomPosition) {
				board.LoadPosition (customPosition);
				searchBoard.LoadPosition (customPosition);
			} else {
				board.LoadStartPosition ();
				searchBoard.LoadStartPosition ();
			}
			onPositionLoaded?.Invoke ();
			boardUI.UpdatePosition (board);
			boardUI.ResetSquareColours ();

			CreatePlayer (ref whitePlayer, whitePlayerType);
			CreatePlayer (ref blackPlayer, blackPlayerType);

			gameResult = Result.Playing;
			PrintGameResult (gameResult);

			NotifyPlayerToMove ();

		}

		void LogAIDiagnostics () {
			string text = "";
			var d = aiSettings.diagnostics;
			//text += "AI Diagnostics";
			text += $"<color=#{ColorUtility.ToHtmlStringRGB(colors[3])}>Version 1.0\n";
			text += $"<color=#{ColorUtility.ToHtmlStringRGB(colors[0])}>Depth Searched: {d.lastCompletedDepth}";
			//text += $"\nPositions evaluated: {d.numPositionsEvaluated}";

			string evalString = "";
			if (d.isBook) {
				evalString = "Book";
			} else {
				float displayEval = d.eval / 100f;
				if (playerToMove is AIPlayer && !board.WhiteToMove) {
					displayEval = -displayEval;
				}
				evalString = ($"{displayEval:00.00}").Replace (",", ".");
				if (Search.IsMateScore (d.eval)) {
					evalString = $"mate in {Search.NumPlyToMateFromScore(d.eval)} ply";
				}
			}
			text += $"\n<color=#{ColorUtility.ToHtmlStringRGB(colors[1])}>Eval: {evalString}";
			text += $"\n<color=#{ColorUtility.ToHtmlStringRGB(colors[2])}>Move: {d.moveVal}";

			aiDiagnosticsUI.text = text;
		}

		public void ExportGame () {
			string pgn = PGNCreator.CreatePGN (gameMoves.ToArray ());
			string baseUrl = "https://www.lichess.org/paste?pgn=";
			string escapedPGN = UnityEngine.Networking.UnityWebRequest.EscapeURL (pgn);
			string url = baseUrl + escapedPGN;

			Application.OpenURL (url);
			TextEditor t = new TextEditor ();
			t.text = pgn;
			t.SelectAll ();
			t.Copy ();
		}

		public void QuitGame () {
			Application.Quit ();
		}

		void NotifyPlayerToMove () {
			gameResult = GetGameState ();
			PrintGameResult (gameResult);

			if (gameResult == Result.Playing) {
				playerToMove = (board.WhiteToMove) ? whitePlayer : blackPlayer;
				playerToMove.NotifyTurnToMove ();
                winloseTxt.text = "";

            } else {
				Debug.Log ("Game Over");
                winloseTxt.text = "Game over";

            }
		}

		void PrintGameResult (Result result) {
			float subtitleSize = resultUI.fontSize * 0.75f;
			string subtitleSettings = $"<color=#787878> <size={subtitleSize}>";

			if (result == Result.Playing) {
				resultUI.text = "";
			} else if (result == Result.WhiteIsMated || result == Result.BlackIsMated) {
				resultUI.text = "Checkmate!";
			} else if (result == Result.FiftyMoveRule) {
				resultUI.text = "Draw";
				resultUI.text += subtitleSettings + "\n(50 move rule)";
			} else if (result == Result.Repetition) {
				resultUI.text = "Draw";
				resultUI.text += subtitleSettings + "\n(3-fold repetition)";
			} else if (result == Result.Stalemate) {
				resultUI.text = "Draw";
				resultUI.text += subtitleSettings + "\n(Stalemate)";
			} else if (result == Result.InsufficientMaterial) {
				resultUI.text = "Draw";
				resultUI.text += subtitleSettings + "\n(Insufficient material)";
			}
		}

		Result GetGameState () {
			MoveGenerator moveGenerator = new MoveGenerator ();
			var moves = moveGenerator.GenerateMoves (board);

			// Look for mate/stalemate
			if (moves.Count == 0) {
				if (moveGenerator.InCheck ()) {
					return (board.WhiteToMove) ? Result.WhiteIsMated : Result.BlackIsMated;
				}
				return Result.Stalemate;
			}

			// Fifty move rule
			if (board.fiftyMoveCounter >= 100) {
				return Result.FiftyMoveRule;
			}

			// Threefold repetition
			int repCount = board.RepetitionPositionHistory.Count ((x => x == board.ZobristKey));
			if (repCount == 3) {
				return Result.Repetition;
			}

			// Look for insufficient material (not all cases implemented yet)
			int numPawns = board.pawns[Board.WhiteIndex].Count + board.pawns[Board.BlackIndex].Count;
			int numRooks = board.rooks[Board.WhiteIndex].Count + board.rooks[Board.BlackIndex].Count;
			int numQueens = board.queens[Board.WhiteIndex].Count + board.queens[Board.BlackIndex].Count;
			int numKnights = board.knights[Board.WhiteIndex].Count + board.knights[Board.BlackIndex].Count;
			int numBishops = board.bishops[Board.WhiteIndex].Count + board.bishops[Board.BlackIndex].Count;

			if (numPawns + numRooks + numQueens == 0) {
				if (numKnights == 1 || numBishops == 1) {
					return Result.InsufficientMaterial;
				}
			}

			return Result.Playing;
		}

		void CreatePlayer (ref Player player, PlayerType playerType) {
			if (player != null) {
				player.onMoveChosen -= OnMoveChosen;
			}

			if (playerType == PlayerType.Human) {
				player = new HumanPlayer (board);
			} else {
				player = new AIPlayer (searchBoard, aiSettings);
			}
			player.onMoveChosen += OnMoveChosen;
		}

        #endregion

        public Client client;
        public GameServerConnect websocket;
        public TMPro.TMP_InputField userName;
        public TMPro.TMP_InputField password;
        public Button login;
        public Button logout;
        public Button createRoom;
        public Button playAIRoom;
		public Button leaveRoom;
		public Button readyToPlay_w;
		public Button readyToPlay_b;
		public Button updateRoom;
		public Transform roomsContent;
		public RoomButton roomButtonPrefab;

		public TMPro.TextMeshProUGUI helloText;
		public TMPro.TextMeshProUGUI whiteTxt;
		public TMPro.TextMeshProUGUI blackTxt;
		public TMPro.TextMeshProUGUI logTxt;
		public TMPro.TextMeshProUGUI turnTxt;
		public TMPro.TextMeshProUGUI winloseTxt;


		public List<RoomButton> roomButtonsCreated = new List<RoomButton>();
		[SerializeField]
		public ClientInformation clientInformation;

        [Sirenix.OdinInspector.Button]
		public string GetFence()
		{
			return FenUtility.CurrentFen(board);

        }
        [Sirenix.OdinInspector.Button]
        public void UpdateFence(string fence)
		{
            if(fence == string.Empty)
            {
                return;
            }
            gameMoves.Clear();
            board.LoadPosition(fence);
            searchBoard.LoadPosition(fence);
            onPositionLoaded?.Invoke();
            boardUI.UpdatePosition(board);
            boardUI.ResetSquareColours();

            CreatePlayer(ref whitePlayer, whitePlayerType);
            CreatePlayer(ref blackPlayer, blackPlayerType);

            gameResult = Result.Playing;
            PrintGameResult(gameResult);
            NotifyPlayerToMove();
            if (board.WhiteToMove)
            {
                turnTxt.text = "White move";
            }
            else
            {
                turnTxt.text = "Black move";
            }


        }
        public enum GameState { Prelogin, Lobby, InRoom, InPlay }
		public GameState gameState;
		public static GameManager Instance;

        private void Awake()
        {
            Instance = this;
            login.onClick.AddListener(()=>StartCoroutine(Login()));
            logout.onClick.AddListener(Logout);
			createRoom.onClick.AddListener(()=> StartCoroutine(CreateRoomCoroutine()));
			leaveRoom.onClick.AddListener(()=> StartCoroutine(LeaveRoomCoroutine()));
			updateRoom.onClick.AddListener(()=> StartCoroutine(UpdateLobbyRoomsCoroutine()));
			readyToPlay_b.onClick.AddListener(()=> StartCoroutine(SetMeReadyCoroutine(true)));
			readyToPlay_w.onClick.AddListener(()=> StartCoroutine(SetMeReadyCoroutine(false)));
            playAIRoom.onClick.AddListener(() => StartCoroutine(CreateRoomCoroutine(true)));
            websocket.onMessageReceivedMove += (message) => ReceiveMoveFromServer(message);
        }
        void Start()
        {
			//Application.targetFrameRate = 60;
			useClocks = false;
            if (useClocks)
            {
                whiteClock.isTurnToMove = false;
                blackClock.isTurnToMove = false;
            }
            userName.text = $"User_{DateTime.Now.ToString()}";
            password.text = $"admin";

            boardUI = FindObjectOfType<BoardUI>();
            gameMoves = new List<Move>();
            board = new Board();
            searchBoard = new Board();
            aiSettings.diagnostics = new Search.SearchDiagnostics();

            NewGame(whitePlayerType, blackPlayerType);
			gameState= GameState.Prelogin;
			InvokeRepeating("UpdateUIGeneral", 0f, 1f);
        }

        void Update()
        {
            zobristDebug = board.ZobristKey;

            if (gameResult == Result.Playing)
            {
                LogAIDiagnostics();

                playerToMove.Update();

                if (useClocks)
                {
                    whiteClock.isTurnToMove = board.WhiteToMove;
                    blackClock.isTurnToMove = !board.WhiteToMove;
                }
            }
            

        }

        public IEnumerator Login()
        {

            if (gameState == GameState.Prelogin)
			{
				JSONNode ticket = null;
				bool isCompleted = false;
				yield return client.LoginCoroutine(userName.text, password.text, (receivedMessage) =>
				{
					ticket = receivedMessage;
				});
                try
				{
					isCompleted = false;
					websocket.onMessageReceived = (message) =>
					{
                        if (message["command"] == "Login" && message["value"] == "Accept")
                        {
							gameState = GameState.Lobby;
                            isCompleted = true;
                        }
                    };
					Task.Run(async () =>
					{
						await websocket.ConnectToGameServer(ticket);
					});
                }
				catch (Exception ex)
                {
                    Debug.LogError(ex.Message);
                    Debug.LogError("Login fail. Please retry");
					yield break;
                }
                yield return new WaitUntil(() => isCompleted);
                websocket.onMessageReceived = null;

				yield return UpdateMyClientInformation();

                yield return UpdateLobbyRoomsCoroutine();

            }
        }

        [Sirenix.OdinInspector.Button]
        public void Logout()
        {
			var task = Task.Run(async () =>
			{
				gameState = GameState.Prelogin;
				await websocket.Disconnect();
				clientInformation = null;
			});
        }


		public IEnumerator UpdateLobbyRoomsCoroutine()
		{
            bool isCompleted = false;
			var roomList = new List<string>();
            websocket.onMessageReceived = (message) =>
            {
				try
				{
                    if (message["command"] == "GetRooms")
                    {
                        var messageValue = message["value"];
						var datas = SimpleJSON.JSON.Parse(messageValue);
						foreach(var btn in roomButtonsCreated)
						{
							Destroy(btn.gameObject);
						}
						roomButtonsCreated.Clear();


                        foreach (var data in datas)
						{
							var btn = Instantiate(roomButtonPrefab, roomsContent);
							btn.InitRoom(data.Key, JoinRoom);
							btn.gameObject.SetActive(true);
							
							roomButtonsCreated.Add(btn);

							Debug.LogError(data);
						}
                        gameState = GameState.Lobby;
                        isCompleted = true;
                    }
                }
				catch
				{

				}
            };

            var task = Task.Run(async () =>
            {
                await websocket.GetRooms();
            });
			yield return new WaitUntil(() => isCompleted);
        }

		public IEnumerator CreateRoomCoroutine(bool aiRoom = false)
        {
            bool isCompleted = false;
            var roomList = new List<string>();
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "CreateRoom" && message["value"] == "Accept")
                    {
                        gameState = GameState.InRoom;
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.CreateRoom(aiRoom);
            });
            yield return new WaitUntil(() => isCompleted);
			yield return UpdateMyRoomStatusCoroutine(true);

        }
        public IEnumerator UpdateMyRoomStatusCoroutine(bool updateFence = false)
        {
            bool isCompleted = false;
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "GetMyRoomStatus")
                    {
						var roomStatus = SimpleJSON.JSON.Parse(message["value"]);
                        if (roomStatus["black"] != null)
						{
                            blackTxt.text = roomStatus["black"];
                        }
						else
						{
							blackTxt.text = "Waiting for player";
						}
                        if (roomStatus["white"] != null)
                        {
                            whiteTxt.text = roomStatus["white"];
                        }
						else
						{
                            whiteTxt.text = "Waiting for player";
                        }
                        if(roomStatus["black"] == clientInformation.gui)
                        {
                            blackTxt.text = "You";
                        }
                        if (roomStatus["white"] == clientInformation.gui)
                        {
                            whiteTxt.text = "You";
                        }
                        if (!roomStatus["whiteReady"] || !roomStatus["blackReady"])
                        {
                            logTxt.text = "Get ready!!!";
                        }
                        else
                        {
                            logTxt.text = "";
                        }


                        if (updateFence)
                        {
                            if (roomStatus["fen"] != null)
                            {
                                UpdateFence(roomStatus["fen"]);
                            }
                        }
                        if (roomStatus["whiteReady"] == true)
                        {
							readyToPlay_w.image.color = Color.green;
                        }
						else
						{
                            readyToPlay_w.image.color = Color.black;
                        }
                        if (roomStatus["blackReady"] == true)
                        {
                            readyToPlay_b.image.color = Color.green;
                        }
                        else
                        {
                            readyToPlay_b.image.color = Color.black;
                        }

                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.GetMyRoomStatus();
            });
            yield return new WaitUntil(() => isCompleted);
        }
        public IEnumerator LeaveRoomCoroutine()
        {
            bool isCompleted = false;
            var roomList = new List<string>();
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "LeaveRoom" && message["value"] == "Accept")
                    {
                        gameState = GameState.Lobby;
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.LeaveRoom();
            });
            yield return new WaitUntil(() => isCompleted);
        }
		public void JoinRoom(string roomName)
		{
			if(gameState == GameState.Lobby)
			{
				StartCoroutine(JoinRoomCouroutine(roomName));
            }
		}
		private IEnumerator JoinRoomCouroutine(string roomName)
		{
            bool isCompleted = false;
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "JoinRoom" && message["value"] == "Accept")
                    {
                        Debug.LogError("JoinRoom accept");
                        gameState = GameState.InRoom;
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.JoinRoom(roomName);
            });
            yield return new WaitUntil(() => isCompleted);
            yield return UpdateMyRoomStatusCoroutine(true);
        }
		private IEnumerator SetMeReadyCoroutine(bool isBlack)
		{
			if(gameState != GameState.InRoom)
			{
				yield break;
			}
            bool isCompleted = false;
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "GetReady" && message["value"] == "Accept")
                    {
                        gameState = GameState.InRoom;
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.SetMeReady();
            });
            yield return new WaitUntil(() => isCompleted);
        }

		private IEnumerator SendMoveToGameServerCoroutine(string move)
		{
			if(gameState != GameState.InRoom)
			{
                UpdateFence("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                yield break;
			}
            bool isCompleted = false;
            string nextFence = string.Empty;
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "Move")
                    {
                        gameState = GameState.InRoom;
                        nextFence = message["value"];
                        UpdateFence(nextFence);
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.MakeAMove(move);
            });
            yield return new WaitUntil(() => isCompleted);
            UpdateMyRoomStatusCoroutine(true);
        }

        private void ReceiveMoveFromServer(JSONNode message)
        {
            if (gameState != GameState.InRoom)
            {
                UpdateFence("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                return;
            }
            if (message["command"] == "Move")
            {
                var nextFence = message["value"];
                UpdateFence(nextFence);
            }
        }

        private IEnumerator UpdateMyClientInformation()
		{
            bool isCompleted = false;
            websocket.onMessageReceived = (message) =>
            {
                try
                {
                    if (message["command"] == "GetMyStatus")
                    {
						var data = SimpleJSON.JSON.Parse(message["value"]);
                        clientInformation = new ClientInformation()
						{
							gui = data["gui"],
							roomid = data["roomid"],
							username = data["username"],
						};
						helloText.text = $"Hello {clientInformation.username}";
                        isCompleted = true;
                    }
                }
                catch
                {

                }
            };
            var task = Task.Run(async () =>
            {
                await websocket.GetMyStatus();
            });
            yield return new WaitUntil(() => isCompleted);
        }
		private void UpdateUIGeneral()
		{
			if(gameState != GameState.InRoom)
			{
                blackTxt.text = "Waiting for player";
                whiteTxt.text = "Waiting for player";
                readyToPlay_b.interactable = false;
                readyToPlay_w.interactable = false;
            }

			if(gameState != GameState.Lobby)
			{
				updateRoom.interactable = false;
				createRoom.interactable = false;
                playAIRoom.interactable = false;
				updateRoom.interactable = false;
				
			}
			else
			{
                updateRoom.interactable = true;
                createRoom.interactable = true;
                playAIRoom.interactable = true;
                updateRoom.interactable = true;
            }

			if(gameState == GameState.Prelogin)
			{
				login.interactable = true;
				logout.interactable = false;
			}
			else
			{
				login.interactable = false;
                logout.interactable = true;
            }

			if(gameState == GameState.InRoom)
			{
                StartCoroutine(UpdateMyRoomStatusCoroutine());
                leaveRoom.interactable = true;
				readyToPlay_w.interactable = true;
				readyToPlay_b.interactable = true;
            }
			else
			{
				leaveRoom.interactable = false;
                readyToPlay_w.interactable = false;
                readyToPlay_b.interactable = false;
            }

			if(gameState == GameState.Lobby)
			{
				StartCoroutine(UpdateLobbyRoomsCoroutine());
            }
            
        }
    }
}
[System.Serializable]
public class ClientInformation
{
	public string username;
	public string roomid;
	public string gui;
}
