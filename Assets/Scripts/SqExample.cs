using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SqExample : MonoBehaviour
{

    private const string SQ_API_CLIENT_ID = "PASTE_CLIENT_ID_HERE";

    public TMPro.TextMeshProUGUI CodeText;
    public GameObject GetCodeButton;
    public GameObject LogOutButton;
    public GameObject RefreshButton;

    SqAppApi sq;

    void Start()
    {
        //construct a new configuration object using the client ID from the sidequest app management page, 
        //      a path where persistent data can be stored, a flag for test mode,
        //      and optionally a filename that will be used to save data
        SqAppApiConfig config = new SqAppApiConfig(SQ_API_CLIENT_ID, Application.persistentDataPath, true);

        //create a new instance of the api class using the config.
        //Only one instance of this class should be created for the scope of the application so that it can properly maintain its state
        sq = new SqAppApi(config);
        SetLoginState();
        if (sq.User != null)
        {
            Debug.Log("User is logged in at startup, refreshing the user's profile");
            RefreshUser();
        }
    }

    public void GetCodeClick()
    {
        GetCodeButton.SetActive(false);
        //call GetLoginCode from the api to retrieve the short code a user should enter
        StartCoroutine(sq.GetLoginCode((code) =>
        {
            Debug.Log("SQ: Successfully got login short code from API");
            //When a code has been retrieved, the Code and the VerificationUrl returned from the API should
            //  be shown to the user
            CodeText.text = $"Go to {code.VerificationUrl}\nput in {code.Code}";

            //begin polling for completion of the short code login using the interval returned from the API
            StartPolling(code.PollIntervalSeconds);
        }, (error) => {
            //if something goes wrong, details of what should be in the exception
            Debug.LogError("Failed to get code from API!");
            Debug.LogException(error);
            GetCodeButton.SetActive(true);
        }));
    }

    private void StopPolling()
    {
        Debug.Log("SQ: Stopping polling for completion of short code login");
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
    }

    private void StartPolling(int delaySec)
    {
        Debug.Log("SQ: Beginning polling for completion of short code login");
        waitCoroutine = StartCoroutine(Poller(delaySec));
    }

    private IEnumerator Poller(int delaySec)
    {
        //this coroutine loops until the short code login request either fails or succeeds, waiting delaySec between checks
        while (true)
        {
            yield return new WaitForSecondsRealtime(delaySec);
            SqUser user = null;
            bool isDone = false;
            Exception ex = null;

            //Call to check if the short code has been completed 
            yield return sq.CheckLoginCodeComplete((done, usr) =>
            {
                //The function is invoked with two parameters:
                // the first (done) is a boolean indicating if the short code request has been completed by the user
                // the second (usr) is the user profile object, and will be null until (done) is true
                isDone = done;
                user = usr;
            }, (e) =>
            {
                ex = e;
            });
            if (ex != null)
            {
                //failures mean the call failed, timed out or something else went wrong.
                //when this happens, stop polling because the situation won't improve.
                Debug.LogError("Exception while checking for login code completion");
                Debug.LogException(ex);
                CodeText.text = $"Failed: {ex.Message}";
                GetCodeButton.SetActive(true);
                StopPolling();
                yield break;
            }
            if (isDone)
            {
                Debug.Log("Login with short code has completed");
                //if the user logged in with the short code, stop the polling coroutine and continue on
                LoginCompleted();
                StopPolling();                
                yield break;
            } else
            {
                Debug.Log($"Login with short code is not yet complete.  Will check again in {delaySec} seconds");
            }
        }
    }

    public void LogOut()
    {
        sq.Logout();
        SetLoginState();
    }

    private void LoginCompleted()
    {
        GetCodeButton.SetActive(false);
        CodeText.text = $"Logged in as: {sq.User.Name}";
        LogOutButton.SetActive(true);
        RefreshButton.SetActive(true);
    }

    public void RefreshUser()
    {
        if (sq.User != null)
        {
            //refreshes a user's data from the API.
            //This should be called periodically (e.g. on app start) to update the user's profile information.
            StartCoroutine(sq.RefreshUserProfile((u) =>
            {
                Debug.Log("User profile information has been refreshed from the API successfully");
                CodeText.text = $"Logged in as: {sq.User.Name}";
            }, (e) =>
            {
                Debug.LogError("Failed to refresh user");
                Debug.LogException(e);
            }));
        }
    }
    private void SetLoginState()
    {
        if (sq.User != null)
        {
            LoginCompleted();
        } else
        {
            GetCodeButton.SetActive(true);
            CodeText.text = "Push Button, Get Code";
            LogOutButton.SetActive(false);
            RefreshButton.SetActive(false);
        }
    }
    Coroutine waitCoroutine;



    
}
