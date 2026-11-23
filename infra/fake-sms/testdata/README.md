# İleti Merkezi API şekilleri (sahte SMS sağlayıcısı)

- Kaynak: İleti Merkezi API dokümantasyonu
  - https://www.iletimerkezi.com/en/docs/api/send-sms
  - https://www.iletimerkezi.com/en/docs/api/overview
  - https://www.iletimerkezi.com/en/docs/api/get-report
  - https://www.iletimerkezi.com/en/docs/api/get-balance
  - https://www.iletimerkezi.com/en/docs/api/error-codes
- Erişim tarihi: 2026-11-23

## Doğrulama durumu

- `send-request.json` / `send-response.json`: dokümandaki istek ve başarılı yanıt alanlarıyla doğrulandı.
- `report-request.json` / `report-response.json`: dokümandaki istek alanı (`request.order.id`) ve
  alıcı durumu dizisi doğrulandı. Alıcı dizisinin anahtarı (`message`) dokümandaki örnekten alındı;
  dokümanın kendisi canlı yanıtla kontrol edilmesini öneriyor. Durum kodları 110/111/112 dokümandan.
- `balance-request.json` / `balance-response.json`: dokümandaki istek ve yanıt alanlarıyla doğrulandı.
  Tutar sayı olarak dönüyor (dokümanın eski sürümleri metin gösterebiliyor).
- `auth-error-response.json` (401) ve `insufficient-balance-response.json` (402): kod ve mesajlar
  hata kodları sayfasından alındı. Dokümantasyon bu hatalar için gövde (JSON) örneği vermiyor;
  gövde, dokümanın genel hata zarfı (`response.status.code` / `message`) varsayılarak yazıldı.
  UNVERIFIED: gerçek sağlayıcı gövdesi ve HTTP durum kodu canlı hesapta doğrulanmalı.
- Alıcı numaraları `905XXXXXXXXX`, `+905XXXXXXXXX` ve `5XXXXXXXXX` biçimlerinde kabul edilir; depolamada `+90…` olarak tutulur.
