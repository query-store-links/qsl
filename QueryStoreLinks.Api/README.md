# QueryStoreLinks.Api

轻量的 ASP.NET Core Web API，用于解析 Microsoft Store 下载链接（基于原 CLI 的 `QueryLinksHelper`）。

主要端点：

- GET /api/links/parse?input={url_or_id}
- POST /api/links/appinfo  -> 获取应用信息（JSON body: { ProductId, Market, Locale }）
- POST /api/links/filelist -> 获取文件列表 XML（JSON body: { Cookie, CategoryId, Ring, WuSoapTemplate }）
- POST /api/links/appx     -> 解析 APPX 包并返回下载项（JSON body: { FileListXml, Ring, UrlSoapTemplate }）
- POST /api/links/nonappx  -> 获取非 APPX 安装包（JSON body: { ProductId, Market }）
- POST /api/links/resolve-all -> 一站式解析（发送 ProductInput 和 SOAP 模板）

示例：

POST /api/links/resolve-all
{
  "productInput": "9NBLGGH4R315",
  "cookieSoap": "<...cookie.xml content...>",
  "wuSoapTemplate": "<...wu.xml content...>",
  "urlSoapTemplate": "<...url.xml content...>",
  "market": "CN",
  "locale": "zh-CN",
  "ring": "Retail"
}

返回 JSON 包含 app info、cookie（EncryptedData）、文件列表 XML、APPX 与 non-appx 下载项与错误列表（若有）。
