namespace MeuClaudeWidget {
 using System.Text.Encodings.Web;
 using System.Text.Json;

 // Substitui o JavaScriptSerializer do .NET Framework com as mesmas formas de resultado:
 // objetos viram Dictionary<string,object>, listas viram object[], números viram long ou double,
 // e campos públicos são lidos e gravados sem distinguir maiúsculas no nome.
 public sealed class Json {
  static readonly JsonSerializerOptions options = new JsonSerializerOptions {
   IncludeFields = true,
   PropertyNameCaseInsensitive = true,
   NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // acentos ficam legíveis nos arquivos locais
   AllowTrailingCommas = true,
   ReadCommentHandling = JsonCommentHandling.Skip,
  };
  static readonly JsonDocumentOptions document = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
  public int MaxJsonLength = 1048576;

  public string Serialize(object value) { return JsonSerializer.Serialize(value, value == null ? typeof(object) : value.GetType(), options); }
  public T Deserialize<T>(string json) { Check(json); return JsonSerializer.Deserialize<T>(json, options); }
  public object DeserializeObject(string json) { Check(json); using (var doc = JsonDocument.Parse(json, document)) return Convert(doc.RootElement); }

  void Check(string json) { if (json != null && json.Length > MaxJsonLength) throw new ArgumentException("JSON maior que o limite de " + MaxJsonLength + " caracteres."); }

  static object Convert(JsonElement e) {
   switch (e.ValueKind) {
    case JsonValueKind.Object: { var map = new Dictionary<string, object>(); foreach (var p in e.EnumerateObject()) map[p.Name] = Convert(p.Value); return map; }
    case JsonValueKind.Array: { var list = new List<object>(); foreach (var item in e.EnumerateArray()) list.Add(Convert(item)); return list.ToArray(); }
    case JsonValueKind.String: return e.GetString();
    case JsonValueKind.Number: { long l; if (e.TryGetInt64(out l)) return l; return e.GetDouble(); }
    case JsonValueKind.True: return true;
    case JsonValueKind.False: return false;
    default: return null;
   }
  }
 }
}
