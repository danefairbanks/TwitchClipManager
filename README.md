# Download and Delete Twitch Clips

Important, need to get your twitch token via inspector due to using gql.  You can get a token by going to twitch in chrome.
While logged in press f12. Go to the **console** tab, and at the cursor type in:

**cookies["auth-token"]**

This will give you the token. Refer to the image below:
https://cdn.discordapp.com/attachments/349982179864084491/719497018401357884/unknown.png

Download compiled for win10 64-bit here: http://danefairbanks.com/downloads/apps/ClipManager.zip


Experimental version here:
http://danefairbanks.com/downloads/apps/ClipManager.0.2.zip

## Experimental version features
Will batch ~ 1000 clips (twitch limitation).  
Can start processing from a low to high view count.
Can Limit what view count you process.

Common use case: Delete clips with low view count
Enable deleting, choose low to high option, set limit to 1000.  
App will delete every clip with less than 1000 views.

Common use case: Download all cliips with high view count
Enable download, choose high to low option, set limit to 1000.
App will download every clip with more than 1000 views.
