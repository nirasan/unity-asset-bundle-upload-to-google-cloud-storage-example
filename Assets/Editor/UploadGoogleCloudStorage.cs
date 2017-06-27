using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System;
using System.Linq;
using System.IO;

public class UploadGoogleCloudStorage
{
	/// <summary>
	/// Create Google Service Account and download P12 key file.
	/// See also https://developers.google.com/identity/protocols/OAuth2ServiceAccount#creatinganaccount
	/// </summary>
	private const string keyFile = "";

	/// <summary>
	/// P12 key password.
	/// </summary>
	private const string keyPassword = "";

	/// <summary>
	/// Service Accounts email address.
	/// </summary>
	private const string email = @"";

	/// <summary>
	/// Budget name to be uploaded.
	/// Budget needs to be created in advance.
	/// </summary>
	private const string badgetName = @"";

	private const string baseDir = "AssetBundles";
	private static readonly BuildTarget[] buildTargets =
		new BuildTarget[] { BuildTarget.iOS, BuildTarget.Android };
	private const string baseUploadUrlFormat = @"https://www.googleapis.com/upload/storage/v1/b/{0}/o?uploadType=media&name={1}";

	[MenuItem ("Build/Create Google JWT Test")]
	public static void CreateJWTTest ()
	{
		Debug.Log (GoogleJsonWebToken.Create (email, Application.dataPath + "/" + keyFile, keyPassword));
	}

	[MenuItem ("Build/Get Access Token Test")]
	public static void GetAccessTokenTest ()
	{
		GetAccessToken ((res) => {
			var getAccessTokenResponse = JsonUtility.FromJson<GetAccessTokenResponse> (res);
			Debug.Log ("access_token is " + getAccessTokenResponse.access_token);
		});
	}

	[MenuItem ("Build/Build AssetBundles And Upload GCS")]
	public static void Build ()
	{
		if (Directory.Exists (baseDir))
		{
			Directory.Delete (baseDir, true);
		}
		Directory.CreateDirectory (baseDir);

		foreach (BuildTarget target in buildTargets)
		{
			BuildForPlatform (target);
		}
	}

	private static void BuildForPlatform (BuildTarget target)
	{
		string dir = GetTargetDir (target);
		Directory.CreateDirectory (dir);

		BuildPipeline.BuildAssetBundles (dir,
			BuildAssetBundleOptions.ChunkBasedCompression,
			target);
		
		Upload (target);
	}

	private static void Upload (BuildTarget target)
	{
		GetAccessToken ((res) => {
			var getAccessTokenResponse = JsonUtility.FromJson<GetAccessTokenResponse> (res);

			var dirInfo = new DirectoryInfo (GetTargetDir (target));
			foreach (var fileInfo in dirInfo.GetFiles())
			{
				Debug.Log ("upload : " + fileInfo.FullName);
				UploadFile (target, fileInfo, getAccessTokenResponse.access_token);
			}
		});
	}

	private static void UploadFile (BuildTarget target, FileInfo fileInfo, string token)
	{
		var headers = new Dictionary<string, string> ();
		headers.Add ("Authorization", "Bearer " + token);
		headers.Add ("Content-Type", "application/octet-stream");

		var filename = GetTargetString (target) + "/" + fileInfo.Name;
		var url = string.Format (baseUploadUrlFormat, badgetName, filename);

		Debug.Log ("Upload url is " + url);
		
		byte[] fileBytes = File.ReadAllBytes (fileInfo.FullName);
		var uploadHandler = new UploadHandlerRaw (fileBytes);

		PostRequest (url, null, uploadHandler, headers, (res) => {
			Debug.Log ("Success: " + res);
		});
	}

	private static string GetTargetString (BuildTarget target)
	{
		return target.ToString ().ToLower ();
	}

	private static string GetTargetDir (BuildTarget target)
	{
		var str = GetTargetString (target);
		return Path.Combine (baseDir, str);
	}

	[Serializable]
	public class GetAccessTokenResponse
	{
		public string access_token;
	}

	public static void GetAccessToken (Action<string> onSuccess)
	{
		var url = @"https://www.googleapis.com/oauth2/v4/token";
		var form = new WWWForm ();
		form.AddField ("grant_type", @"urn:ietf:params:oauth:grant-type:jwt-bearer");
		form.AddField ("assertion", GoogleJsonWebToken.Create (email, Application.dataPath + "/" + keyFile, keyPassword));
		PostRequest (url, form, null, null, onSuccess);
	}
	
	public static bool PostRequest (string url, WWWForm form, UploadHandler uploadHandler, Dictionary<string, string> headers, Action<string> onSuccess)
	{
		using (UnityWebRequest r = UnityWebRequest.Post (url, form))
		{
			if (uploadHandler != null)
			{
				r.uploadHandler = uploadHandler;
			}

			if (headers != null)
			{
				foreach (var header in headers)
				{
					r.SetRequestHeader (header.Key, header.Value);
				}
			}

			return SendPostRequest (r, onSuccess);
		}
	}

	private static bool SendPostRequest (UnityWebRequest r, Action<string> onSuccess)
	{
		var s = r.Send ();
		long startTick = DateTime.Now.Ticks;
		while (!s.isDone)
		{
			if (DateTime.Now.Ticks > startTick + 10L * 10000000L)
			{
				Debug.LogWarning ("Timeout");
				break;
			}
		}

		if (string.IsNullOrEmpty (r.error))
		{
			Debug.Log ("POST : " + r.url);
			Debug.Log (r.downloadHandler.text);
			if (onSuccess != null) onSuccess (r.downloadHandler.text);
			return true;
		}
		else
		{
			Debug.LogWarning ("Error : " + r.error);
			return false;
		}
	}
}

public class GoogleJsonWebToken
{
	[Serializable]
	public class GoogleJWTHeader
	{
		public string alg;
		public string typ;
	}

	[Serializable]
	public class GoogleJWTPayload
	{
		public string iss;
		public string scope;
		public string aud;
		public int exp;
		public int iat;
	}

	public static string Create (string email, string certificateFilePath, string password)
	{
		var utc0 = new DateTime (1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
		var issueTime = DateTime.UtcNow;

		var iat = (int)issueTime.Subtract (utc0).TotalSeconds;
		var exp = (int)issueTime.AddMinutes (60).Subtract (utc0).TotalSeconds;

		var payload = new GoogleJWTPayload {
			iss = email,
			scope = @"https://www.googleapis.com/auth/devstorage.read_write",
			aud = @"https://www.googleapis.com/oauth2/v4/token",
			exp = exp,
			iat = iat
		};

		var certificate = new X509Certificate2 (certificateFilePath, password);
		var rsa = (RSACryptoServiceProvider)certificate.PrivateKey;

		return Encode (payload, rsa);
	}

	public static string Encode (object payload, RSACryptoServiceProvider rsa)
	{
		var segments = new List<string> ();
		var header = new GoogleJWTHeader{ alg = "RS256", typ = "JWT" };

		byte[] headerBytes = Encoding.UTF8.GetBytes (JsonUtility.ToJson (header));
		byte[] payloadBytes = Encoding.UTF8.GetBytes (JsonUtility.ToJson (payload));

		segments.Add (Base64UrlEncode (headerBytes));
		segments.Add (Base64UrlEncode (payloadBytes));

		var stringToSign = string.Join (".", segments.ToArray ());

		var bytesToSign = Encoding.UTF8.GetBytes (stringToSign);

		byte[] signature = rsa.SignData (bytesToSign, "SHA256");
		segments.Add (Base64UrlEncode (signature));

		return string.Join (".", segments.ToArray ());
	}

	// from JWT spec
	private static string Base64UrlEncode (byte[] input)
	{
		var output = Convert.ToBase64String (input);
		output = output.Split ('=') [0]; // Remove any trailing '='s
		output = output.Replace ('+', '-'); // 62nd char of encoding
		output = output.Replace ('/', '_'); // 63rd char of encoding
		return output;
	}
}

