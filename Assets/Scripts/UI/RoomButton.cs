using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RoomButton : MonoBehaviour
{
    public string roomName;
    public TMPro.TextMeshProUGUI roomTxt;
    public System.Action<string> onBtnClick;
    public Button myBtn;

    private void OnValidate()
    {
        myBtn = GetComponent<Button>();
    }
    public void InitRoom(string roomName,System.Action<string> onClick)
    {
        this.roomName = roomName;
        roomTxt.text = roomName;
        onBtnClick = onClick;
        myBtn.onClick.RemoveAllListeners();
        myBtn.onClick.AddListener(() =>
        {
            onBtnClick?.Invoke(this.roomName);
        });
    }
}
