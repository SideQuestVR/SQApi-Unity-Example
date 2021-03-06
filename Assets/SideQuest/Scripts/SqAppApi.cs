﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Debug = System.Diagnostics.Debug;

/// <summary>
/// Class for interacting with the SideQuest API
/// </summary>
public class SqAppApi
{
    /// <summary>
    /// Create a new instance
    /// </summary>
    /// <param name="config">The configuration options that should be used</param>
    public SqAppApi(SqAppApiConfig config)
    {
        Config = config;
        LoadData();
    }

    /// <summary>
    /// The currently logged in sidequest user's details, or null if a user is not logged in
    /// </summary>
    public SqUser User
    {
        get
        {
            return Data.User;
        }
    }

    /// <summary>
    /// Get a list of the currently logged in sidequest user's achievements
    /// </summary>
    public List<SqUserAchievement> UserAchievements
    {
        get
        {
            return Data.UserAchievements;
        }
    }

    /// <summary>
    /// The currently active short code information or null if no short code login is in progress
    /// </summary>
    public SqLoginCode CurrentLoginCode
    {
        get
        {
            return Data.LoginCode;
        }
    }

    /// <summary>
    /// The configuration being used
    /// </summary>
    public SqAppApiConfig Config { get; private set; }

    /// <summary>
    /// Clears the current user and any active short code requests
    /// </summary>
    public void Logout()
    {
        var wasUserNull = Data?.Token == null;
        Data.Token = null;
        Data.User = null;
        Data.LoginCode = null;
        Data.UserAchievements = null;

        SaveData();
        if (!wasUserNull)
        {
            //todo: raise some event for this?
        }
    }

    /// <summary>
    /// Clears the current short code login request
    /// </summary>
    public void ClearLoginCode()
    {
        if (Data.LoginCode != null)
        {
            Data.LoginCode = null;
            SaveData();
        }
    }

    /// <summary>
    /// Gets login code information and begins the shortcode login process with default scopes
    /// </summary>
    /// <param name="OnCompleted">Function invoked with the resulting short code login when the call is successful</param>
    /// <param name="OnError">Function invoked with the exception when the call fails</param>
    public IEnumerator GetLoginCode(Action<SqLoginCode> OnCompleted, Action<Exception> OnError)
    {
        yield return GetLoginCode(new string[] { SqAuthScopes.ReadBasicProfile, SqAuthScopes.ReadAppAchievements, SqAuthScopes.WriteAppAchievements }, OnCompleted, OnError);
    }

    /// <summary>
    /// Gets login code information and begins the shortcode login process for requesting specific scopes
    /// </summary>
    /// <param name="scopes">The list of scopes to request from the user</param>
    /// <param name="OnCompleted">Function invoked with the resulting short code login when the call is successful</param>
    /// <param name="OnError">Function invoked with the exception when the call fails</param>
    public IEnumerator GetLoginCode(IEnumerable<string> scopes, Action<SqLoginCode> OnCompleted, Action<Exception> OnError)
    {
        _lastLoginPoll = DateTime.MinValue;
        yield return JsonPost<SqLoginCode>("/v2/oauth/getshortcode", new
        {
            client_id = Config.ClientId,
            scopes = scopes.ToArray()
        }, (c) =>
        {
            Data.LoginCode = c;
            SaveData();
            OnCompleted?.Invoke(c);
        }, (e) =>
        {
            OnError?.Invoke(e);
        }, false);
    }
    
    /// <summary>
    /// Checks whether a shortcode login (started with GetLoginCode) has been completed by the user
    /// </summary>
    /// <param name="OnCompleted">Invoked when the check completes successfully with the parameters (completed, user).  completed will be false and user will be null until the user completes the login using the short code.  When the short code login is completed by the user, true and the user object will be passed</param>
    /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
    public IEnumerator CheckLoginCodeComplete(Action<bool, SqUser> OnCompleted, Action<Exception> OnError)
    {
        if (Data.LoginCode == null)
        {
            OnError?.Invoke(new InvalidOperationException("There is not a code login in progress"));
            yield break;
        }
        if (DateTimeOffset.Now > Data.LoginCode.ExpiresAt)
        {
            OnError?.Invoke(new SqApiAuthException("Device code has expired"));
            yield break;
        }
        //check to make sure this isn't being called too frequently
        if ((DateTime.Now - _lastLoginPoll).TotalSeconds < Data.LoginCode.PollIntervalSeconds)
        {
            OnCompleted?.Invoke(false, null);
            yield break;
        }
        SqTokenInfo tok = null;
        Exception ex = null;
        yield return JsonPost<SqTokenInfo>("/v2/oauth/checkshortcode", new { code = Data.LoginCode.Code, device_id = Data.LoginCode.DeviceId },
            (t) => {
                tok = t;
            },
            (e) => {
                ex = e;
            }, false);
        if (ex == null)
        {
            if (tok == null)
            {
                _lastLoginPoll = DateTime.Now;
                OnCompleted?.Invoke(false, null);
                yield break;
            }
            Data.User = null;
            Data.Token = tok;
            ex = null;
            yield return GetUserProfile((u) =>
            {
                Data.User = u;
            }, (e) =>
            {
                ex = e;

            });
            if (ex != null)
            {
                OnError?.Invoke(ex);
            } else
            {
                Data.LoginCode = null;
            
                if (Data?.Token?.GrantedScopes?.Contains(SqAuthScopes.ReadAppAchievements) ?? false)
                {
                    yield return RefreshUserAchievements(c => { }, e => ex = e);
                    if (ex != null)
                    {
                        OnError?.Invoke(new SqApiException("Unable to refresh achievements", ex));
                        yield break;
                    }
                }
                SaveData();
                OnCompleted?.Invoke(true, Data.User);
            }
        }
        else
        {
            OnError?.Invoke(ex);
        }
    }

    /// <summary>
    /// Refreshes the currently logged in user's profile
    /// </summary>
    /// <param name="OnCompleted">Function invoked with the refreshed user's profile</param>
    /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
    public IEnumerator RefreshUserProfile(Action<SqUser> OnCompleted, Action<Exception> OnError)
    {
        SqUser user = null;
        Exception ex = null;
        yield return GetUserProfile((u) => user = u, e => ex = e);
        if (ex != null)
        {
            OnError(ex);
            yield break;
        }
        
        if (user?.UserId != Data.Token?.UserId)
        {
            OnError?.Invoke(new SqApiException("User refreshed data does not match user token ID!"));
            yield break;
        }
        Data.User = user;
        SaveData();
        if (Data?.Token?.GrantedScopes?.Contains(SqAuthScopes.ReadAppAchievements)??false)
        {
            yield return RefreshUserAchievements(c => { }, e => ex = e);
            if (ex != null)
            {
                OnError?.Invoke(new SqApiException("Unable to refresh achievements", ex));
                yield break;
            }
        }
        OnCompleted?.Invoke(user);        
    }

    /// <summary>
    /// Refreshes and returns a list of achievements a user has completed for the app
    /// </summary>
    /// <param name="OnCompleted">Function invoked with the refreshed list of user achievements</param>
    /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
    public IEnumerator RefreshUserAchievements(Action<List<SqUserAchievement>> OnCompleted, Action<Exception> OnError)
    {
        List<SqUserAchievement> achievements = null;
        Exception ex = null;
        yield return JsonGet<List<SqUserAchievement>>("/v2/users/me/apps/me/achievements",
            (a) => achievements = a,
            (e) => ex = e,
            true);
        if (ex != null)
        {
            OnError?.Invoke(ex);
            yield break;
        } else
        {
            Data.UserAchievements = achievements;
            SaveData();
            OnCompleted?.Invoke(achievements);
        }
    }

    /// <summary>
    /// Refreshes and returns a list of available app achievements that the user may or may not have completed
    /// </summary>
    /// <param name="OnCompleted">Function invoked with the refreshed list of user achievements</param>
    /// <param name="OnError">Function invoked with the provoking exception when something goes wrong</param>
    public IEnumerator GetAppAchievements(Action<List<SqAchievement>> OnCompleted, Action<Exception> OnError)
    {
        List<SqAchievement> achievements = null;
        Exception ex = null;
        yield return JsonGet<List<SqAchievement>>("/v2/apps/me/achievements",
            (a) => achievements = a,
            (e) => ex = e,
            true);
        if (ex != null)
        {
            OnError?.Invoke(ex);
            yield break;
        }
        else
        {
             OnCompleted?.Invoke(achievements);
        }
    }

    /// <summary>
    /// Adds an achievement to a user, optionally throwing an exception if it already exists
    /// </summary>
    /// <param name="achievementID">The ID of the achievement to add to the user</param>
    /// <param name="OnCompleted">Function invoked with the resulting user achievement when adding the achievement to the user has succeeded
    ///                 NOTE: if the user token does not have achievement read scope, null will be returned</param>
    /// <param name="OnError">Function invoked with the provoking exception when something goes wrong.</param>
    /// <param name="throwIfAlreadyExists">If true and an achievement is being added to a user that</param>
    /// <returns></returns>
    public IEnumerator AddUserAchievement(string achievementID, Action<SqUserAchievement> OnCompleted, Action<Exception> OnError, bool throwIfAlreadyExists = false)
    {
        Exception ex = null;

        yield return JsonPost<string>("/v2/users/me/apps/me/achievements", new { achievement_identifier = achievementID, achieved = true }, o =>
            {
            }, e => ex = e, true);
        if (ex != null)
        {
            var apiex = ex as SqApiException;
            if (!(apiex != null && apiex.HttpCode == 409 && !throwIfAlreadyExists))
            {
                OnError?.Invoke(ex);
                yield break;
            }
        }
        if (Data?.Token?.GrantedScopes?.Contains(SqAuthScopes.ReadAppAchievements) ?? false)
        {
            yield return RefreshUserAchievements(c =>
            {
                var found = c.FirstOrDefault(x => string.Compare(achievementID, x.AchievementId, true) == 0);
                if (found == null)
                {
                    OnError?.Invoke(new SqApiException("User achievement was added, but was not returned from the server after being added"));
                }
                else
                {
                    OnCompleted(found);
                }

            }, e =>
            {
                OnError?.Invoke(e);
            });
        }
        else
        {
            OnCompleted?.Invoke(null);
        }
    }

    private SqPersistentData _data;
    private SqPersistentData Data
    {
        get
        {
            if (_data == null)
            {
                _data = new SqPersistentData();
            }
            return _data;
        }
        set
        {
            _data = value;
        }
    }

    private IEnumerator GetUserProfile(Action<SqUser> OnCompleted, Action<Exception> OnError)
    {
        if (Data.Token == null)
        {
            OnError?.Invoke(new SqApiAuthException("No user logged in."));
            yield break;
        }
        //todo: get user
        yield return JsonGet<SqUser>($"/v2/users/me", (u) =>
        {
            if (u == null)
            {
                OnError?.Invoke(new SqApiException("User could not be retrieved"));
                return;
            }
            OnCompleted?.Invoke(u);
        }, OnError, true);
    }

    private IEnumerator GetAuthToken(Action<string> OnCompleted, Action<Exception> OnError)
    {
        if (Data?.Token?.AccessTokenExpiresAt == null)
        {
            OnError?.Invoke(new SqApiAuthException("No user is logged in"));
            yield break;
        }
        if (DateTimeOffset.Now < Data.Token.AccessTokenExpiresAt.Value.AddMinutes(-1) && !string.IsNullOrWhiteSpace(Data.Token.AccessToken))
        {
            OnCompleted?.Invoke(Data.Token.AccessToken);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(Data?.Token?.RefreshToken))
        {
            Logout();
            OnError?.Invoke(new SqApiAuthException("User refresh token is missing, logging user out"));
            yield break;
        }
        yield return PostFormEncodedStringNoAuth<SqTokenInfo>("/v2/oauth/token", $"grant_type=refresh_token&refresh_token={UnityWebRequest.EscapeURL(Data.Token?.RefreshToken)}&client_id={Data.Token?.ClientId}", 
            (a) =>
            {
                if (a == null || a.AccessToken == null)
                {
                    OnError?.Invoke(new SqApiAuthException("Failed to retrieve auth token"));
                    return;
                }
                Data.Token.AccessToken = a.AccessToken;
                Data.Token.AccessTokenExpiresAt = a.AccessTokenExpiresAt;
                SaveData();
                OnCompleted?.Invoke(Data.Token.AccessToken);
            }, OnError);
    }

    private IEnumerator PostFormEncodedStringNoAuth<T>(string urlPath, string data, Action<T> OnCompleted, Action<Exception> OnError)
    {
        using (UnityWebRequest req = new UnityWebRequest(new Uri(Config.RootApiUri, urlPath)))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data))
            {
                contentType = "application/x-www-form-urlencoded"
            };
            req.method = "POST";
            req.downloadHandler = new DownloadHandlerBuffer();

            yield return req.SendWebRequest();

            if (req.isNetworkError)
            {
                OnError(new SqApiNetworkException($"Unity Network Error: {req.error}"));
                yield break;
            }
            else if (req.isHttpError)
            {
                if (req.responseCode == 401 || req.responseCode == 403)
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
                else
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
            }

            var resStr = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(resStr))
            {
                OnCompleted?.Invoke(default(T));
                yield break;
            }
            else
            {
                try
                {
                    OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                    yield break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed deserializing response from API", ex);
                    OnError?.Invoke(ex);
                    yield break;
                }
            }
        }
    }

    private IEnumerator JsonGet<T>(string urlPath, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(new Uri(Config.RootApiUri, urlPath)))
        {
            req.SetRequestHeader("Content-Type", "application/json");
            if (Data?.Token != null && withAuth)
            {
                string authToken = null;
                Exception error = null;
                yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                if (error != null)
                {
                    OnError?.Invoke(error);
                    yield break;
                }
                req.SetRequestHeader("Authorization", "Bearer " + authToken);
            }

            yield return req.SendWebRequest();
            if (req.isNetworkError)
            {
                OnError(new SqApiNetworkException($"Unity Network Error: {req.error}"));
                yield break;
            }
            else if (req.isHttpError)
            {
                if (req.responseCode == 401 || req.responseCode == 403)
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
                else
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
            }
            var resStr = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(resStr))
            {
                OnCompleted?.Invoke(default(T));
                yield break;
            }
            else
            {
                try
                {
                    OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                    yield break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed deserializing response from API", ex);
                    OnError?.Invoke(ex);
                    yield break;
                }
            }
        }
    }

    private IEnumerator JsonPost<T>(string urlPath, object data, Action<T> OnCompleted, Action<Exception> OnError, bool withAuth = true)
    {
        // The whole UnitytWebRequest.Put then changing method to POST thing is a janky workaround for JSON posting being broken in Unity...
        using (UnityWebRequest req = UnityWebRequest.Put(new Uri(Config.RootApiUri, urlPath), JsonConvert.SerializeObject(data)))
        {
            req.method = "POST";
            req.SetRequestHeader("Content-Type", "application/json");
            if (Data?.Token != null && withAuth)
            {
                string authToken = null;
                Exception error = null;
                yield return GetAuthToken((a) => authToken = a, (e) => error = e);
                if (error != null)
                {
                    OnError?.Invoke(error);
                    yield break;
                }
                req.SetRequestHeader("Authorization", "Bearer " + authToken);
            }

            yield return req.SendWebRequest();
            if (req.isNetworkError)
            {
                OnError(new SqApiNetworkException($"Unity Network Error: {req.error}"));
                yield break;
            }
            else if (req.isHttpError)
            {
                if (req.responseCode == 401 || req.responseCode == 403)
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
                else
                {
                    OnError(new SqApiAuthException((int)req.responseCode, $"Unity Http Error: {req.error}"));
                    yield break;
                }
            }
            if (req.responseCode == 204)
            {
                OnCompleted?.Invoke(default(T));
                yield break;
            }
            var resStr = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(resStr))
            {
                OnCompleted?.Invoke(default(T));
                yield break;
            }
            else
            {
                try
                {
                    OnCompleted?.Invoke(JsonConvert.DeserializeObject<T>(resStr));
                    yield break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Failed deserializing response from API", ex);
                    OnError?.Invoke(ex);
                    yield break;
                }
            }
        }
    }

    

    private DateTime _lastLoginPoll = DateTime.MinValue;
    

    private void LoadData()
    {

        if (File.Exists(Config.DataFile))
        {
            try
            {
                var data = JsonConvert.DeserializeObject<SqPersistentData>(File.ReadAllText(Config.DataFile));
                Data = data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to load data file", ex);
            }
        }
    }

    private void SaveData()
    {
        File.WriteAllText(Config.DataFile, JsonConvert.SerializeObject(Data));
    }
}

