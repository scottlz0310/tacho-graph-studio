namespace TachoGraphStudio.App.Templates;

// テンプレート一括取り込み(#60)の入力。Json が null のときは view 層でファイルの
// 読み込みに失敗したことを表し、取り込み失敗として集約される
public sealed record TemplateImportFile(string FileName, string? Json);
