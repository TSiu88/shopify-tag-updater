using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ShopifyTagUpdater
{
    class Program
    {
        // Toggle if test mode console logs visible and if using production or sandbox
        private static bool testingMode = Convert.ToBoolean(ConfigurationManager.AppSettings["testMode"].ToString());
        private static bool readValuesFromDB = Convert.ToBoolean(ConfigurationManager.AppSettings["readValuesFromDB"].ToString());
        static readonly bool ProductionAPIMode = Convert.ToBoolean(ConfigurationManager.AppSettings["useProductionAPI"].ToString());

        // Delay milliseconds to use to prevent reaching rate limit
        private static int delayTime = Int32.Parse(ConfigurationManager.AppSettings["delayAmount"].ToString());

        // API and DB Connections (Default)
        private static string apiKey = ConfigurationManager.AppSettings["apiKeyProd"].ToString();
        private static string password = ConfigurationManager.AppSettings["passwordProd"].ToString();
        private static string apiHostString = ConfigurationManager.AppSettings["apiHostStringProd"].ToString();
        private static string apiVersion = ConfigurationManager.AppSettings["apiVersionProd"].ToString();

        private static string SFCon = ConfigurationManager.AppSettings["strSFConnProd"].ToString();
        static readonly string CRCCon = ConfigurationManager.ConnectionStrings["DBConnection"].ConnectionString;

        // Stored Procedures
        private static readonly string GetInputProductsProcedure = ConfigurationManager.AppSettings["getInputProductsProcedure"].ToString();
        private static readonly string LogUpdateTagsStoredProcedure = ConfigurationManager.AppSettings["logUpdateTagsStoredProcedure"].ToString();

        // API Call Results
        private static string getCallFields = ConfigurationManager.AppSettings["getCallSpecificFields"].ToString();
        private static int statusInd = 0;
        private static string callStatus = "";
        private static string CallErrorStatus = "";
        private static string errorMessage = "";

        // Json serializer settings to ignore null values/missing values
        private static JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        // Global variables
        private static List<Dictionary<string, string>> UpdateProductList = new List<Dictionary<string, string>>();

        static void Main(string[] args)
        {
            // Required for API call due to SSL error
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Switch between production and sandbox variables for API depending on toggle
            if (ProductionAPIMode == true)
            {
                apiKey = ConfigurationManager.AppSettings["apiKeyProd"].ToString();
                password = ConfigurationManager.AppSettings["passwordProd"].ToString();
                apiHostString = ConfigurationManager.AppSettings["apiHostStringProd"].ToString();
                apiVersion = ConfigurationManager.AppSettings["apiVersionProd"].ToString();

                SFCon = ConfigurationManager.AppSettings["strSFConnProd"].ToString();
            }
            else
            {
                apiKey = ConfigurationManager.AppSettings["apiKeySandbox"].ToString();
                password = ConfigurationManager.AppSettings["passwordSandbox"].ToString();
                apiHostString = ConfigurationManager.AppSettings["apiHostStringSandbox"].ToString();
                apiVersion = ConfigurationManager.AppSettings["apiVersionSandbox"].ToString();

                SFCon = ConfigurationManager.AppSettings["strSFConnSandbox"].ToString();
            }

            // Get products and tags for update
            if (readValuesFromDB == false)
            {
                // Individual test values when no table/stored procedure to read products from
                // VALUES: "shopify_product_id, add/remove action, tags"
                Dictionary<string, string> testDictionary = new Dictionary<string, string>() 
                { 
                   { "shopify_product_id", "123456789" },
                   { "action", "add" },
                   { "tags", "testTag" }
                };
                UpdateProductList.Add(testDictionary);

                Dictionary<string, string> testDictionary2 = new Dictionary<string, string>()
                {
                    { "shopify_product_id", "6581631385735" },
                    { "action", "remove" },
                    { "tags", "YBlocklist" }
                };
                UpdateProductList.Add(testDictionary2);
            }
            else
            {
                // TODO: Remove from else when table/stored procedure to read products and tags to add/remove are created
                GetProductList();
            }

            // GET EACH PRODUCT TO UPDATE TAGS
            UpdateTags();

            // END OF PROGRAM
            if (testingMode == true)
            {
                // For viewing console logs
                Console.WriteLine("-------- END LOG --------");
                Console.ReadLine();
            }
        }

        // Read products and tags from table/stored procedure for tag updating
        private static void GetProductList()
        {
            // Holds this row's keys and values for the API POST call with data from a table
            Dictionary<string, string> rowValues = new Dictionary<string, string>();

            SqlConnection db = new SqlConnection(CRCCon);

            try
            {
                db.Open();

                SqlCommand cmd = new SqlCommand(GetInputProductsProcedure, db);
                cmd.CommandType = CommandType.StoredProcedure;
                SqlDataReader reader = cmd.ExecuteReader();

                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        rowValues = new Dictionary<string, string>();
                        object[] valueArray = new object[reader.FieldCount];
                        int columnNum = reader.GetValues(valueArray);

                        // Get each column's name and value in a row from the stored procedure and put into dictionary with the correct column name
                        //TODO: Make sure database column names match names used in Shopify
                        for (int i = 0; i < columnNum; i++)
                        {
                            string columnName = reader.GetName(i);
                            rowValues.Add(columnName, valueArray[i].ToString());
                        }

                        // Default to adding tags if not specified
                        if (rowValues.ContainsKey("action") == false)
                        {
                            rowValues.Add("action", "add");
                        }

                        if (testingMode == true)
                        {
                            // When testing write what was read from database in console
                            Console.WriteLine(String.Format("READ: Shopify Product ID: {0}, {1} tags: {2}", rowValues.ElementAt(0).Value, rowValues.ElementAt(1).Value.ToUpper(), rowValues.ElementAt(2).Value));
                        }

                        // Add dictionary of rowValues to list 
                        UpdateProductList.Add(rowValues);
                        Console.WriteLine($"ADDED TO LIST: {rowValues.ElementAt(0).Value}");
                    }
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine("Error in getting products for input: " + ex.Message);
            }
            finally
            {
                db.Close();
                db.Dispose();
            }
        }

        // Update tags for each product in the list
        private static void UpdateTags()
        {
            // For each product in the list
            foreach (Dictionary<string, string> productInput in UpdateProductList) 
            {
                // Get single product's current tags with GET API call
                string currentTags = GetCurrentTags(productInput);

                // Only continue adding/removing tags and updating if no errors
                if (errorMessage == "")
                {
                    // Append or Remove new tag(s) given in input
                    string newTags = "";
                    if (productInput["action"] == "remove")
                    {
                        newTags = RemoveTags(productInput, currentTags);
                    }
                    else
                    {
                        newTags = AddTags(productInput, currentTags);
                    }

                    // Update single product's tags with PUT API call
                    UpdateTagList(productInput, newTags);
                }
                else
                {
                    // Log error with getting product
                    if (testingMode == false)
                    {
                        // TODO: Remove from if/else once stored procedures created
                        LogUpdateTagsStatus(productInput["shopify_product_id"], statusInd, callStatus, errorMessage);
                    }
                    else
                    {
                        Console.WriteLine(String.Format($"Test Log Update Here: {0}, {1}, {2}, {3}", productInput["shopify_product_id"], statusInd, callStatus, errorMessage));
                    }
                }

                // Clear variables for next product in loop
                ClearVariables();
            }
        }

        // Get current product's current tags with API GET call and return tags in string
        // GET /admin/api/2021-07/products/{shopify_product_id}.json?fields={field1,field2...}
        // Required fields: shopify_product_id (URL), specific fields (optional)
        private static string GetCurrentTags(Dictionary<string, string> productInput)
        {
            // Call Shopify API to get product info
            var apiGetProductInfoCall = CallApi("products/", productInput["shopify_product_id"], "METHOD.GET", "");
            var jsonProductInfoResults = apiGetProductInfoCall.Result;

            // Need to deserialize to get current tags
            string currentTags = GetDeserializedItemString(jsonProductInfoResults, "getTags");
            return currentTags;
        }

        // Make call to API and return json string
        public static async Task<string> CallApi(string section, string id, string method, string payload)
        {
            // Production API Request URL Format: https://{API_KEY}:{PASSWORD}@loversonline.myshopify.com/admin/api/{VERSION}/{SECTION}/{ID}.json(?fields={FIELD1,FIELD2})
            // Sandbox API Request URL Format: https://{API_KEY}:{PASSWORD}@lovers-sandbox.myshopify.com/admin/api/{VERSION}/{SECTION}/{ID}.json(?fields={FIELD1,FIELD2})
            RestClient client = new RestClient(apiHostString);
            client.Authenticator = new HttpBasicAuthenticator($"{apiKey}", $"{password}");
            RestRequest request;

            if (method == "METHOD.PUT")
            {
                // Update product tags
                request = new RestRequest($"{apiVersion}/{section}{id}.json", Method.PUT);
                request.AddParameter("application/json; charset=utf-8", payload, ParameterType.RequestBody);
            }
            else
            {
                // GET API call that does not require payload
                // TODO: Test that this works
                if (getCallFields != "")
                {
                    request = new RestRequest($"{apiVersion}/{section}{id}.json?fields={getCallFields}", Method.GET);
                }
                else
                {
                    // Get entire product
                    request = new RestRequest($"{apiVersion}/{section}{id}.json", Method.GET);
                }
            }

            var response = await client.ExecuteAsync(request);
            callStatus = response.StatusDescription.ToString();

            if (callStatus == "OK" || callStatus == "Created")
            {
                statusInd = 1;
                Console.WriteLine($"Successful call for Shopify product {id} {method} with payload {payload}");
            }
            else
            {
                statusInd = 2;
                CallErrorStatus = response.StatusDescription;
                Console.WriteLine($"Error for Shopify product {id} {method}, Status Code: " + response.StatusDescription);
            }
            return response.Content;
        }

        // Deserialize json string and target a single endpoint to return as a string
        public static string GetDeserializedItemString(string jsonResults, string actionType)
        {
            dynamic jsonResponse = JsonConvert.DeserializeObject<JObject>(jsonResults, settings);
            string tagString = "";

            // If has error in API call, don't deserialize and instead return error message from JSON

            if (CallErrorStatus != "")
            {
                tagString = "N/A: Errors in API Call";
                if (jsonResponse.ContainsKey("errors"))
                {
                    // Parse out of json "errors" and add to logstring/itemstring
                    // { "errors": "Not Found" }
                    try
                    {
                        string longError = jsonResponse["errors"].ToString();
                        // Limit error message to be 250 characters w/ "..." at the end if longer than 250
                        if (longError.Length > 249)
                        {
                            errorMessage = longError.Substring(0, 246) + "...";
                        }
                        else
                        {
                            errorMessage = longError;
                        }

                        if (testingMode == true)
                        {
                            Console.WriteLine("API Error: " + errorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMessage = "Unable to deserialize: " + ex;
                        Console.WriteLine(errorMessage);
                    }
                }
                else
                {
                    Console.WriteLine("Unexpected unreadable error in JSON");
                    errorMessage = "Unreadable error in JSON desearializer";
                }
            }
            else if (actionType == "getTags" || actionType == "updateTags")
            {
                // Deserialize JSON for product info
                string productId = jsonResponse["product"]["id"].ToString();
                tagString = jsonResponse["product"]["tags"].ToString();
                
                if (testingMode == true)
                {
                    Console.WriteLine($"Deserialized JSON for product id={productId} with tags={tagString}");
                }
            }
            else
            {
                // General deserialization
                string productString = jsonResponse["product"].ToString();
                Console.WriteLine("Unrecognized action but deserializable: " + productString);
            }
            return tagString;
        }

        // Add tags to string received from API call
        private static string AddTags(Dictionary<string, string> productInput, string currentTags)
        {
            string newTags = "";

            // Check if need to add comma or already have one
            if (currentTags.EndsWith(","))
            {
                newTags = currentTags + productInput["tags"];
            }
            else
            {
                newTags = currentTags + ", " + productInput["tags"];
            }

            if (testingMode == true)
            {
                Console.WriteLine($"New tags list: {newTags}");
            }

            return newTags;
        }

        // Remove tags from string received from API call
        // NOTE: Not used yet
        // TODO: Check that this works
        private static string RemoveTags(Dictionary<string, string> productInput, string currentTags)
        {
            // Parse current tags into list
            string[] currTags = currentTags.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            List<string> tagList = new List<string>(currTags);

            // Parse tags to remove if needed
            string[] removeTags = productInput["tags"].Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            List<string> tagsToRemove = new List<string>(removeTags);

            // Search for tags specified to be removed and remove from list
            foreach (string toRemove in tagsToRemove)
            {
                int removeIndex = tagList.IndexOf(toRemove);
                if (removeIndex == -1)
                {
                    // String to remove not found in tag list
                    Console.WriteLine($"Tag {toRemove} not found, unable to remove");
                }
                else
                {
                    tagList.RemoveAt(removeIndex);

                    if (testingMode == true)
                    {
                        Console.WriteLine($"Tag {toRemove} removed");
                    }
                }
            }
            // Change list back to comma delimited string and return new tag string
            string newTags = string.Join(", ", tagList);

            if (testingMode == true)
            {
                Console.WriteLine($"New tags list: {newTags}");
            }

            return newTags;
        }

        // Build API call payload and Update product's tags with API PUT call
        // PUT /admin/api/2021-07/products/{shopify_product_id}.json
        // Required fields: shopify_product_id (URL and payload), tags (payload)
        private static void UpdateTagList (Dictionary<string, string> productInput, string newTags)
        {
            string productId = productInput["shopify_product_id"];
            string tagPayload = "{ \"product\": { \"id\": " + productId + ", \"tags\": \""+ newTags + "\" } }";

            if (testingMode == true)
            {
                Console.WriteLine($"Update payload: {tagPayload}");
            }

            // Call Shopify API to update tags
            var apiPutUpdateCall = CallApi("products/", productId, "METHOD.PUT", tagPayload);
            var jsonUpdateResults = apiPutUpdateCall.Result;

            // Need to deserialize to get current tags and set status in deserializer
            string currentTags = GetDeserializedItemString(jsonUpdateResults, "updateTags");

            // Log whether update was successful or not
            if (testingMode == false)
            {
                // TODO: Remove from if/else once stored procedures created
                LogUpdateTagsStatus(productId, statusInd, callStatus, errorMessage);
            }
            else
            {
                Console.WriteLine(String.Format("Test Log Update Here: {0}, {1}, {2}, {3}", productId, statusInd, callStatus, errorMessage));
            }

            Console.WriteLine($"Product for id: {productId}, updated with tags: {currentTags}");
        }

        // Log status indicator for if tags were adding/removed successfully or errors
        private static void LogUpdateTagsStatus(string productId, int updateIndicator, string responseStatus, string errorDescription)
        {
            SqlConnection db = new SqlConnection(CRCCon);
            SqlCommand cmd = new SqlCommand(LogUpdateTagsStoredProcedure, db);
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add("@OrderID", SqlDbType.Int).Value = Int32.Parse(productId);
            cmd.Parameters.Add("@SFUpdateInd", SqlDbType.Int).Value = updateIndicator;
            cmd.Parameters.Add("@ResponseStatus", SqlDbType.VarChar, 50).Value = responseStatus;
            cmd.Parameters.Add("@ErrorDescription", SqlDbType.VarChar, 250).Value = errorDescription;

            try
            {
                db.Open();
                cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                Console.WriteLine("ERROR: Tag Update Logging failed: " + ex.Message);
            }
            finally
            {
                Console.WriteLine("Tag Update Logged Successfully!");
                db.Close();
                db.Dispose();
            }
        }

        // Clear variables between products
        private static void ClearVariables()
        {
            statusInd = 0;
            callStatus = "";
            errorMessage = "";
            CallErrorStatus = "";
        }
    }
}
