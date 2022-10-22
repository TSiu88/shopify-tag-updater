# Shopify Tag Updater
### Initiated on 08.17.2021, Updated on 10.21.2022
### By: Tiffany Siu

## ABOUT
This is a console application that uses the Shopify API to update tags in a product that already exists in Shopify.  First an API Get call is used to get the current tags (GET .../admin/api/2021-07/products/{shopify_product_id}.json).  Then the new tags are appened/tags are removed from the list.  Lastly an API Put call is used to replace the tags with the new list of tags (PUT .../admin/api/2021-07/products/{shopify_product_id}.json with payload of product > id, tags).

Need to update Shopify and database credentials and create stored procedures and tables.

To make an API call for Shopify API, the general syntax is:
- `https://{API_KEY}:{PASSWORD}@{SITE}/admin/api/{VERSION}/{RESOURCE}.json{FILTERS}`


## KNOWN BUGS
No known bugs at this time.

## TECHNOLOGIES USED
- C#
- .NET Framework
- SQL Server Management Studio
- MySQL Workbench
- [Shopify Admin API](https://shopify.dev/docs/admin-api)
	- [Shopify Admin Product API](https://shopify.dev/api/admin/rest/reference/products/product)
- [RestSharp](https://restsharp.dev/)
- [NewtonSoft](https://www.newtonsoft.com/json)
- [Postman](https://www.postman.com/downloads/)


## ASSOCIATED RESOURCES USED
### ACCESS NEEDED
- Shopify MySQL Production Database
- Main records database
- Shopify Production API

## CHANGE LOG
| Date		| Name			| Description |
| --------- | ------------- | ----------- |
| 08.17.2021	| Tiffany Siu		| Initial build with setup variables, outline structure, start input reading from DB |
| 08.18.2021	| Tiffany Siu		| Write class to read input from DB, Add class for GET API call and deserializing results, Add class to add tags to list |
| 08.19.2021	| Tiffany Siu		| Add class to remove tags from list, add class to update tags |
| 08.20.2021	| Tiffany Siu		| Add class to log success/errors, clear variables, test |
| 10.11.2022	| Tiffany Siu		| Clean up code, remove identifying info, update README |
| 10.14.2022	| Tiffany Siu		| Update comments, fix boolean for using stored procedures or test values, fix error logging |
| 10.21.2022	| Tiffany Siu		| Update README |

## VERSION HISTORY
| Date		| Version		| Description |
| --------- | ------------- | ----------- |
| 08.20.2020	| 1.0			| Initial build |

## CONTACT & SUPPORT
Tiffany Siu: tsiu88@gmail.com