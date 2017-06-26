* Example of uploading Unity's Asset Bundle to Google Cloud Storage

# How to use

* Create google service account.
  * See also https://developers.google.com/identity/protocols/OAuth2ServiceAccount#creatinganaccount .
  * Create project if needed.
* Download P12 private key file.
  * And save p12 file "Assets" directory.
* Create google cloud storage budget to be uploaded.
* Set constants, "keyFile", "keyPassword", "email", and "badgetName".
* Build asset bundles and upload from "Build/Build AssetBundles And Upload GCS".

