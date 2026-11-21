import { Platform, Share } from 'react-native';

/**
 * Copies text to clipboard on Web or opens the native Share sheet on iOS/Android
 * without requiring additional native clipboard packages.
 */
export async function copyOrShareText(text: string): Promise<'copied' | 'shared' | 'failed'> {
  if (Platform.OS === 'web') {
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return 'copied';
      }
    } catch {
      return 'failed';
    }
    return 'failed';
  }

  try {
    await Share.share({ message: text });
    return 'shared';
  } catch {
    return 'failed';
  }
}
