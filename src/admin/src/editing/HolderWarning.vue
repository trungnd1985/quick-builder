<script setup lang="ts">
// Someone else is editing this document (023 research CE-17): publishing goes ahead with the
// saved draft, without their unsaved changes; deleting stops their editing; copying a version
// into the draft makes their next save a conflict.
import { useI18n } from 'vue-i18n'
import './editing.css'

defineProps<{ name: string; action: 'publish' | 'delete' | 'copyToDraft' }>()
const { t } = useI18n()
</script>

<template>
  <p
    class="editing-warning"
    role="note"
  >
    {{ t(`editing.warnings.${action}`, { name }) }}
  </p>
</template>
