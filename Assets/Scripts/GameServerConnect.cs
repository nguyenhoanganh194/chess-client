using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;
using System.Threading.Tasks;
using SimpleJSON;
using System;

public class GameServerConnect : MonoBehaviour
{
    WebSocket websocket;
    public string url = "ws://localhost:8080";
    public System.Action<JSONNode> onMessageReceived = null;   
    public System.Action<JSONNode> onMessageReceivedMove = null;   

    // Start is called before the first frame update
    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
#endif
    }
    private async void OnApplicationQuit()
    {
        await Disconnect();
    }

    public async Task ConnectToGameServer(JSONNode ticket)
    {
        Debug.LogError("Connecting");
        url = ticket["gameserver"];
        var headerList = new Dictionary<string, string>()
        {
            { "token", ticket["token"]},
            { "gui", ticket["id"] }
        };
        websocket = new WebSocket(url, headerList);

        websocket.OnMessage += (bytes) =>
        {
            var message = System.Text.Encoding.UTF8.GetString(bytes);
            var messegeData = SimpleJSON.JSON.Parse(message);
            onMessageReceived?.Invoke(messegeData);
            onMessageReceivedMove?.Invoke(messegeData);
        };
        websocket.OnOpen += () =>
        {
            Debug.Log("Connection open!");
        };

        websocket.OnError += (e) =>
        {
            Debug.Log("Error! " + e);
        };
        websocket.OnClose += (e) =>
        {
            Debug.Log("Connection closed! " + e);
        };

        await websocket.Connect();
    }

    public async Task CreateRoom(bool isAIRoom)
    {
        var command = new WSMessage()
        {
            context = "Lobby",
            command = "CreateRoom",
            value = isAIRoom.ToString(),
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }

    public async Task GetRooms()
    {
        var command = new WSMessage()
        {
            context = "Lobby",
            command = "GetRooms"
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }

    public async Task JoinRoom(string roomId)
    {
        if (websocket.State == WebSocketState.Open)
        {
            var command = new WSMessage()
            {
                context = "Lobby",
                command = "JoinRoom",
                value = roomId
            };
            var data = JsonUtility.ToJson(command);
            await SendMessageToGameServer(data);
        }
    }

    public async Task GetMyRoomStatus()
    {
        var command = new WSMessage()
        {
            context = "Room",
            command = "GetMyRoomStatus"
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }


    public async Task GetMyStatus()
    {
       
        var command = new WSMessage()
        {
            command = "GetMyStatus"
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
        
    }




    public async Task LeaveRoom()
    {
        var command = new WSMessage()
        {
            context = "Room",
            command = "LeaveRoom"
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }

    public async Task SetMeReady()
    {
        var command = new WSMessage()
        {
            context = "Room",
            command = "GetReady"
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }
    public async Task MakeAMove(string move)
    {
        var command = new WSMessage()
        {
            context = "Room",
            command = "Move",
            value = move
        };
        var data = JsonUtility.ToJson(command);
        await SendMessageToGameServer(data);
    }
    public async Task Disconnect()
    {
        await websocket.Close();
    }

    [Sirenix.OdinInspector.Button]
    private async Task SendMessageToGameServer(string message)
    {
        if (websocket.State == WebSocketState.Open)
        {
            await websocket.SendText(message);
        }
    }




    public class WSMessage
    {
        public string context;
        public string command;
        public string value;
    }
}
