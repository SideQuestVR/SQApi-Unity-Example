# SideQuest API Unity Example
This is example code of how to use the SideQuestVR API to link a SideQuest user's profile to a unity app via a short code login.

To make use of this example, you will need an API client ID which will require an approved app listing on sidequestvr.com and/or access to the SideQuest test environment.
For more information, check the SideQuest developer discord https://sdq.st/devdiscord or the primary Discord https://sdq.st/discord

To get an API client ID:
* log into your developer account on sidequest or the test environment and "MANAGE" your app.
* Under the "Manage Listing" section, generate an API client ID in the "SideQuest API Credentials section"
* Copy the client ID and paste it into the SQ_API_CLIENT_ID constant at the top of SqExample.cs

## General workflow:
Create a new instance of SqAppApiConfig with the appropriate api client key, persistent storage path, test mode flag, and optionally a specific filename to store data in
Create a new singleton instance, shared throughout the application, of SqAppApi using the config

After app launch, check to see if sqApi.User is set to a value.

 If sqApi.User has a value, then a user is logged in.
     Call sqApi.RefreshUserProfile to update the cached user profile.  
     If the call fails with a SqApiAuthException, the user likely revoked their access and is no longer logged in
     
When linking a sidequest user to your app:
     Call sqApi.GetLoginCode to retrieve the codeRequest.Code and codeRequest.VerificationUrl that a user can use to link their account
     
     Poll every codeRequest.PollIntervalSeconds seconds and:
         call CheckLoginCodeComplete.
         If it completes with "false", the user has not yet completed the process, and polling should continue
         If it completes with "true" and a user object, the user has successfully linked their account and their profile is saved locally.  polling should stop.
         If OnError is invoked, either the short code login timed out or something has gone wrong. polling should stop, and an error should be shown to the user to retry.

To log a user out, call sqApi.Logout().  This will clear any persisted storage about the user and any short code logins.
