/// アバターパラメータ定義(docs/protocol.md「1. アバターパラメータ定義」と一致させること)。
enum ParamType { boolean, integer }

class PhoneParameter {
  const PhoneParameter(this.name, this.type, {this.min = 0, this.max = 0});

  final String name;
  final ParamType type;
  final int min;
  final int max;
}

class PhoneParameters {
  PhoneParameters._();

  static const String visible = 'Phone/Visible';
  static const String connected = 'Phone/Connected';
  static const String locked = 'Phone/Locked';
  static const String page = 'Phone/Page';
  static const String pose = 'Phone/Pose';
  static const String battery = 'Phone/Battery';
  static const String callState = 'Phone/CallState';
  static const String mediaState = 'Phone/MediaState';
  static const String notifyType = 'Phone/NotifyType';
  static const String eventToggle = 'Phone/EventToggle';

  static const List<PhoneParameter> all = [
    PhoneParameter(visible, ParamType.boolean),
    PhoneParameter(connected, ParamType.boolean),
    PhoneParameter(locked, ParamType.boolean),
    PhoneParameter(page, ParamType.integer, min: 0, max: 7),
    PhoneParameter(pose, ParamType.integer, min: 0, max: 5),
    PhoneParameter(battery, ParamType.integer, min: 0, max: 10),
    PhoneParameter(callState, ParamType.integer, min: 0, max: 4),
    PhoneParameter(mediaState, ParamType.integer, min: 0, max: 4),
    PhoneParameter(notifyType, ParamType.integer, min: 0, max: 4),
    PhoneParameter(eventToggle, ParamType.boolean),
  ];

  /// UI表示用ラベル(Page 0-7)。
  static const List<String> pageNames = [
    'ロック', 'ホーム', '通知', '通話', 'カメラ', 'メディア', '設定', '接続エラー',
  ];

  /// UI表示用ラベル(Pose 0-5)。
  static const List<String> poseNames = [
    '収納', '右手', '左手', '耳あて', '自撮り', '両手',
  ];

  /// バッテリー残量% → 0-10 の11段階(docs/protocol.md「値の意味」)。
  static int batteryLevelToStep(int percent) {
    if (percent <= 4) return 0;
    final step = ((percent + 5) / 10).floor();
    return step.clamp(0, 10);
  }
}
