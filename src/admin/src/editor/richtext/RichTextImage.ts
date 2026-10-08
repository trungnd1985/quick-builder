import { defineComponent, h, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaApi } from '../../api/media'
import { defaultSource, mediaDetails } from '../blocks/image/mediaDetails'

/**
 * A rich text image on the canvas (035 US3): drawn from the admin's addresses, as the Image
 * block is, with a placeholder when its file is gone, which a published page leaves out.
 */
export default defineComponent({
  name: 'RichTextImage',
  props: {
    siteId: { type: String, required: true },
    mediaId: { type: String, required: true },
    alt: { type: String, default: '' },
  },
  setup(props) {
    const { t } = useI18n()
    const src = ref<string | null>(null)
    const missing = ref(false)

    watch(() => [props.siteId, props.mediaId] as const, async ([siteId, mediaId]) => {
      missing.value = false
      src.value = mediaApi.previewUrl(siteId, mediaId)
      const file = await mediaDetails(siteId, mediaId)
      if (props.mediaId !== mediaId) return
      if (file === null) missing.value = true
      else src.value = defaultSource(file) ?? src.value
    }, { immediate: true })

    return () => h('figure', { class: 'qb-rt-image' }, [
      missing.value || !src.value
        ? h('div', { class: 'qb-editor-placeholder' }, t('editor.blocks.placeholder.imageMissing'))
        : h('img', { src: src.value, alt: props.alt, draggable: 'false', onError: () => { missing.value = true } }),
    ])
  },
})
