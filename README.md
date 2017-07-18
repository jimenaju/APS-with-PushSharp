# APS with PushSharp
A Windows Service implementation of Apple Push Services using [PushSharp](https://github.com/Redth/PushSharp)

# App.config
Update the `CertificateLocation` and `CertificatePassword` values in the App.config file

# APS.Data / PushQueue
I have a DAL project that's NOT in the respoitory that handles a queue of messages to send.  
You'll probably want to create your own DAL and have the following methods. 

```C#
// Removes a token from the DB
static void PushQueue.RemoveToken(string deviceToken);

// Get's the list of PushQueue items
static List<PushQueue> PushQueue.Read();

// Removes the item from the queue
void Delete();
```
# PushQueue Class
```C#
public string alert_message { get; set; }
public int badge { get; set; }
public string device_token { get; set; }
// My project specific properties below:
public int id { get; set; }
public Nullable<int> magazine_id { get; set; }
public string issue_id { get; set; }
```
# EntityFramework
I'm using EF in my DAL project (APS.Data) so the main project App.config has references to EF along with connection strings.
