cd "C:\Users\xyang\Local\Core\Github\Local\CORE.Gleanathon.GleanInside\ShapeDiver\ChatbotComponent\ChatbotComponent"

rd /s /q bin
rd /s /q obj

dotnet restore ChatbotComponent.csproj
dotnet build ChatbotComponent.csproj