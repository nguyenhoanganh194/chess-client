using SimpleJSON;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;


public class Client : MonoBehaviour
{
    public TMPro.TMP_InputField http_server;
    public string HTTP_ADDRESS = "http://127.0.0.1:8001/login";
    public static string API_KEY = "Api-key a83eead7-fd2b-49dd-9c33-5d6c33ad8925";
    public bool isLogin;
 

    //co2, Sound,  
    private void Awake()
    {
        http_server.text = HTTP_ADDRESS;
        RSACryptoService.GenerateNewRSAP();
    }

    public IEnumerator LoginCoroutine(string username, string password,System.Action<JSONNode> onCompleted )
    {
        HTTP_ADDRESS = http_server.text;
        UnityWebRequest getKeybRequest = UnityWebRequest.Get(HTTP_ADDRESS);
        yield return getKeybRequest.SendWebRequest();
        var serverKey = string.Empty;
        switch (getKeybRequest.result)
        {
            case UnityWebRequest.Result.ConnectionError:
            case UnityWebRequest.Result.DataProcessingError:
                break;
            case UnityWebRequest.Result.ProtocolError:
                break;
            case UnityWebRequest.Result.Success:
                serverKey = getKeybRequest.downloadHandler.text;
                break;
        }
        var loginData = new LoginData()
        {
            username = username,
            password = password,
            apiKey = API_KEY
        };
        string json = JsonUtility.ToJson(loginData);
        var encrypt = RSACryptoService.EncryptData(json, serverKey);

        WWWForm form = new WWWForm();
        form.AddField("data", encrypt);
        form.AddField("publickey", RSACryptoService.GetPublicKey());

        UnityWebRequest webRequest = UnityWebRequest.Post(HTTP_ADDRESS, form);
        yield return webRequest.SendWebRequest();

        string[] pages = HTTP_ADDRESS.Split('/');
        int page = pages.Length - 1;

        switch (webRequest.result)
        {
            case UnityWebRequest.Result.ConnectionError:
            case UnityWebRequest.Result.DataProcessingError:
                Debug.LogError(pages[page] + ": Error: " + webRequest.error);
                break;
            case UnityWebRequest.Result.ProtocolError:
                Debug.LogError(pages[page] + ": HTTP Error: " + webRequest.error);
                break;
            case UnityWebRequest.Result.Success:
                var stringData = RSACryptoService.DecryptData(webRequest.downloadHandler.text);
                var data = SimpleJSON.JSON.Parse(stringData);
                onCompleted?.Invoke(data);
                break;
        }
        
    }
    public class LoginData
    {
        public string username;
        public string password;
        public string apiKey;
    }
}