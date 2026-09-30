# Icon Generator

[English](README.md) | [한국어](README_KO.md) | 日本語

> VRChatのアバターやアクセサリーのアイコンを作成するUnityエディターツールです。

## 動作環境

- Unity 2022.3
- Modular Avatar（任意）

## インストール

[VPMリポジトリ](https://koz39.github.io/vpm-listing/)で「Add to VCC」を押し、VCCまたはALCOMでIcon Generatorを追加します。

## 使い方

1. 上部メニューの Tools > Icon Generator を開きます。
2. ソースにアバターやアクセサリーをドラッグ＆ドロップします。
3. プレビューを見ながらカメラとアイコンの設定を調整します。
4. 「アイコンを生成」をクリックします。

## 主な機能

- 合成アイコンと個別アイコンを一度に生成
- ソースツリーで撮影する項目をチェック・選択して範囲を指定
- プレビュー上でマウスによるカメラの移動・回転・ズーム
- 項目ごとに異なるカメラ設定
- 見える部分を基準にした構図、見切れ防止、背景色、輪郭線
- ファイル名テンプレートと重複ファイルの処理
- Modular AvatarのShape Changer、Material Setter、Material Swapを反映
- 保存したアイコンをMAメニューのアイコンに自動で設定

## v1からのアップデート

- メニューの場所が Tools > 3D Obj to Icon から Tools > Icon Generator に変わりました。
- 保存フォルダ、アイコンサイズ、ズーム、カメラ角度、言語の設定は初回起動時に自動で引き継がれます。
- デフォルトのファイル名が `{name}.png` に変わり、v1で作成したアイコンを上書きしません。
